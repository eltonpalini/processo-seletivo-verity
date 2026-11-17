using CashFlow.Domain.Entities;

namespace CashFlow.Domain.Interfaces;

public interface ITransactionRepository
{
    Task<bool> TryAddIdempotentAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
