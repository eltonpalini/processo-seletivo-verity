using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Services;

public interface IDailyBalanceQueryService
{
    Task<DailyBalanceResponse> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public sealed class DailyBalanceQueryService : IDailyBalanceQueryService
{
    private readonly IDailyBalanceCacheService _cacheService;
    private readonly IDailyBalanceRepository _dailyBalanceRepository;
    private readonly ILogger<DailyBalanceQueryService> _logger;

    public DailyBalanceQueryService(
        IDailyBalanceCacheService cacheService,
        IDailyBalanceRepository dailyBalanceRepository,
        ILogger<DailyBalanceQueryService> logger)
    {
        _cacheService = cacheService;
        _dailyBalanceRepository = dailyBalanceRepository;
        _logger = logger;
    }

    public async Task<DailyBalanceResponse> GetByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // 1. Tenta ler primeiro do Cache Distribuído (Redis)
        var cached = await _cacheService.GetAsync(date, cancellationToken);
        if (cached is not null)
        {
            return cached with { Source = "RedisCache" };
        }

        // 2. Fallback para o Banco de Dados Relacional (Fonte da Verdade com dado já pré-computado)
        var entity = await _dailyBalanceRepository.GetByDateAsync(date, cancellationToken);
        if (entity is null)
        {
            return new DailyBalanceResponse(
                date,
                TotalCredits: 0m,
                TotalDebits: 0m,
                NetBalance: 0m,
                TransactionCount: 0,
                LastUpdatedAtUtc: DateTimeOffset.UtcNow,
                Source: "Empty");
        }

        var response = new DailyBalanceResponse(
            entity.Date,
            entity.TotalCredits,
            entity.TotalDebits,
            entity.NetBalance,
            entity.TransactionCount,
            entity.LastUpdatedAtUtc,
            Source: "Database");

        // Repopula o Redis (Cache-Aside)
        await _cacheService.SetAsync(response with { Source = "RedisCache" }, cancellationToken);

        _logger.LogDebug("Saldo do dia {Date} recuperado do banco de dados e sincronizado no cache.", date);

        return response;
    }
}
