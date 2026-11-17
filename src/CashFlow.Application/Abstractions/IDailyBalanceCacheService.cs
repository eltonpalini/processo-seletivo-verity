using CashFlow.Application.DTOs;

namespace CashFlow.Application.Abstractions;

public interface IDailyBalanceCacheService
{
    Task<DailyBalanceResponse?> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task SetAsync(DailyBalanceResponse balance, CancellationToken cancellationToken = default);
}
