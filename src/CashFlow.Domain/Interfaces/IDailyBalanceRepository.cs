using CashFlow.Domain.Entities;
using CashFlow.Domain.Enums;

namespace CashFlow.Domain.Interfaces;

public interface IDailyBalanceRepository
{
    Task<DailyBalance?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplica o lançamento no saldo consolidado do dia de forma atômica e idempotente.
    /// Caso o <paramref name="transactionId"/> já tenha sido consolidado anteriormente,
    /// ignora a soma duplicada e retorna o saldo atual.
    /// </summary>
    Task<(DailyBalance Balance, bool WasApplied)> ConsolidateTransactionIdempotentAsync(
        Guid transactionId,
        DateOnly date,
        decimal amount,
        TransactionType type,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);
}
