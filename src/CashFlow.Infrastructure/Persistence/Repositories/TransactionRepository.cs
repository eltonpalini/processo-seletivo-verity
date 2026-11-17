using CashFlow.Domain.Entities;
using CashFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CashFlow.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly CashFlowDbContext _dbContext;
    private readonly ILogger<TransactionRepository> _logger;

    public TransactionRepository(CashFlowDbContext dbContext, ILogger<TransactionRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> TryAddIdempotentAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Transactions
            .AsNoTracking()
            .AnyAsync(x => x.Id == transaction.Id, cancellationToken);

        if (exists)
        {
            return false;
        }

        try
        {
            await _dbContext.Transactions.AddAsync(transaction, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Conflito de chave primária detectado ao gravar lançamento {TransactionId}. Tratando como idempotente.",
                transaction.Id);
            _dbContext.Entry(transaction).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.Date == date)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
