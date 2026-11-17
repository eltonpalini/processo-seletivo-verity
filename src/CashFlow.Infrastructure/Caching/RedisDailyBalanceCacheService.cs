using System.Text.Json;
using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CashFlow.Infrastructure.Caching;

public sealed class RedisDailyBalanceCacheService : IDailyBalanceCacheService
{
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<RedisDailyBalanceCacheService> _logger;

    public RedisDailyBalanceCacheService(
        IDistributedCache distributedCache,
        ILogger<RedisDailyBalanceCacheService> logger)
    {
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<DailyBalanceResponse?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(date);

        try
        {
            var payload = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            return JsonSerializer.Deserialize<DailyBalanceResponse>(payload, JsonOptions);
        }
        catch (Exception ex)
        {
            // Degradação graciosa: se o Redis cair, o fluxo segue para o PostgreSQL (Fonte da Verdade)
            _logger.LogWarning(
                ex,
                "Falha ao consultar o cache Redis para a chave {CacheKey}. Acionando fallback para o banco de dados.",
                key);
            return null;
        }
    }

    public async Task SetAsync(DailyBalanceResponse balance, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(balance.Date);

        try
        {
            // Protecao contra escrita fora de ordem: workers concorrentes podem tentar gravar um saldo
            // mais antigo depois de um mais novo. TransactionCount so cresce ao longo do dia, entao um
            // valor ja em cache com contagem maior representa um estado mais recente e nao deve ser sobrescrito.
            var currentPayload = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (!string.IsNullOrWhiteSpace(currentPayload))
            {
                var current = JsonSerializer.Deserialize<DailyBalanceResponse>(currentPayload, JsonOptions);
                if (current is not null && current.TransactionCount > balance.TransactionCount)
                {
                    _logger.LogDebug(
                        "Cache {CacheKey} ja contem um saldo mais recente ({Cached} lancamentos > {Incoming}). Escrita ignorada.",
                        key, current.TransactionCount, balance.TransactionCount);
                    return;
                }
            }

            var payload = JsonSerializer.Serialize(balance, JsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultCacheTtl
            };

            await _distributedCache.SetStringAsync(key, payload, options, cancellationToken);
        }
        catch (Exception ex)
        {
            // Se o Redis estiver indisponível no momento da consolidação, não interrompe o Worker:
            // o dado já está salvo no PostgreSQL (Fonte da Verdade).
            _logger.LogWarning(
                ex,
                "Não foi possível atualizar o cache Redis para a chave {CacheKey}. O dado permanece íntegro no banco de dados.",
                key);
        }
    }

    private static string BuildKey(DateOnly date) => $"cashflow:daily-balance:{date:yyyy-MM-dd}";
}
