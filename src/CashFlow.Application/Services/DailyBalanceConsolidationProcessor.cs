using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Application.Messages;
using CashFlow.Domain.Common;
using CashFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Services;

public interface IDailyBalanceConsolidationProcessor
{
    Task<Result<DailyBalanceResponse>> ProcessAsync(
        TransactionRecordedEventMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Caso de uso executado pelo Worker 2 (ConsolidationWorker):
/// 1. Consome o evento de lançamento gravado da Fila 2 (consolidation-queue)
/// 2. Atualiza de forma atômica e idempotente o saldo consolidado diário no PostgreSQL (Fonte da Verdade)
/// 3. Atualiza o cache distribuído no Redis para leitura imediata pela Web API
/// </summary>
public sealed class DailyBalanceConsolidationProcessor : IDailyBalanceConsolidationProcessor
{
    private readonly IDailyBalanceRepository _dailyBalanceRepository;
    private readonly IDailyBalanceCacheService _cacheService;
    private readonly ILogger<DailyBalanceConsolidationProcessor> _logger;

    public DailyBalanceConsolidationProcessor(
        IDailyBalanceRepository dailyBalanceRepository,
        IDailyBalanceCacheService cacheService,
        ILogger<DailyBalanceConsolidationProcessor> logger)
    {
        _dailyBalanceRepository = dailyBalanceRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<Result<DailyBalanceResponse>> ProcessAsync(
        TransactionRecordedEventMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.TransactionId == Guid.Empty || message.Amount <= 0)
        {
            return Result.Failure<DailyBalanceResponse>(
                "Consolidation.InvalidMessage",
                "Dados do evento de consolidação são inválidos.");
        }

        var (balance, wasApplied) = await _dailyBalanceRepository.ConsolidateTransactionIdempotentAsync(
            message.TransactionId,
            message.Date,
            message.Amount,
            message.Type,
            message.RecordedAtUtc,
            cancellationToken);

        var responseDto = new DailyBalanceResponse(
            balance.Date,
            balance.TotalCredits,
            balance.TotalDebits,
            balance.NetBalance,
            balance.TransactionCount,
            balance.LastUpdatedAtUtc,
            "RedisCache");

        // Sincroniza o cache distribuído (Redis) com o novo saldo já computado
        await _cacheService.SetAsync(responseDto, cancellationToken);

        if (wasApplied)
        {
            _logger.LogInformation(
                "Saldo do dia {Date} consolidado pelo lançamento {TransactionId}. Novo saldo líquido: {NetBalance:C2} ({Count} lançamentos).",
                balance.Date,
                message.TransactionId,
                balance.NetBalance,
                balance.TransactionCount);
        }
        else
        {
            _logger.LogInformation(
                "Lançamento {TransactionId} já havia sido consolidado anteriormente para o dia {Date} (idempotência garantida).",
                message.TransactionId,
                balance.Date);
        }

        return Result.Success(responseDto);
    }
}
