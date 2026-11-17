using CashFlow.Domain.Entities;
using CashFlow.Domain.Enums;
using CashFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Infrastructure.Persistence.Repositories;

public sealed class DailyBalanceRepository : IDailyBalanceRepository
{
    private readonly CashFlowDbContext _dbContext;

    public DailyBalanceRepository(CashFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DailyBalance?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DailyBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Date == date, cancellationToken);
    }

    public async Task<(DailyBalance Balance, bool WasApplied)> ConsolidateTransactionIdempotentAsync(
        Guid transactionId,
        DateOnly date,
        decimal amount,
        TransactionType type,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var alreadyProcessed = await _dbContext.ProcessedTransactions
            .AsNoTracking()
            .AnyAsync(x => x.TransactionId == transactionId, cancellationToken);

        if (alreadyProcessed)
        {
            var existingBalance = await _dbContext.DailyBalances
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Date == date, cancellationToken)
                ?? DailyBalance.CreateForDate(date, occurredAtUtc);

            return (existingBalance, false);
        }

        var isRelational = _dbContext.Database.IsRelational();
        await using var dbTransaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (isRelational)
        {
            // Lock transacional no PostgreSQL particionado por data (DayNumber):
            // serializa atualizações concorrentes apenas para o mesmo dia (evitando Lost Updates e colisões),
            // enquanto dias diferentes processam em paralelo.
            await _dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0});",
                [date.DayNumber],
                cancellationToken);

            // Revalida a idempotência já dentro do lock transacional
            var alreadyProcessedUnderLock = await _dbContext.ProcessedTransactions
                .AsNoTracking()
                .AnyAsync(x => x.TransactionId == transactionId, cancellationToken);

            if (alreadyProcessedUnderLock)
            {
                var currentBalance = await _dbContext.DailyBalances
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Date == date, cancellationToken)
                    ?? DailyBalance.CreateForDate(date, occurredAtUtc);

                return (currentBalance, false);
            }
        }

        var balance = await _dbContext.DailyBalances
            .FirstOrDefaultAsync(x => x.Date == date, cancellationToken);

        if (balance is null)
        {
            balance = DailyBalance.CreateForDate(date, occurredAtUtc);
            await _dbContext.DailyBalances.AddAsync(balance, cancellationToken);
        }

        var applyResult = balance.ApplyTransaction(amount, type, occurredAtUtc);
        if (applyResult.IsFailure)
        {
            throw new InvalidOperationException(applyResult.Error.Message);
        }

        var processedRecord = new ProcessedTransaction(transactionId, date, DateTimeOffset.UtcNow);
        await _dbContext.ProcessedTransactions.AddAsync(processedRecord, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (dbTransaction is not null)
        {
            await dbTransaction.CommitAsync(cancellationToken);
        }

        return (balance, true);
    }
}
