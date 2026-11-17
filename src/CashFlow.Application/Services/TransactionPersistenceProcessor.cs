using CashFlow.Application.Abstractions;
using CashFlow.Application.Messages;
using CashFlow.Domain.Common;
using CashFlow.Domain.Entities;
using CashFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Services;

public interface ITransactionPersistenceProcessor
{
    Task<Result> ProcessAsync(
        CreateTransactionCommandMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Caso de uso executado pelo Worker 1 (TransactionWorker):
/// 1. Consome o lançamento da Fila 1 (transactions-queue)
/// 2. Persiste de forma idempotente no banco de dados (PostgreSQL)
/// 3. Dispara o gatilho (evento) para a Fila 2 (consolidation-queue)
/// </summary>
public sealed class TransactionPersistenceProcessor : ITransactionPersistenceProcessor
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IMessageBusPublisher _messageBusPublisher;
    private readonly ILogger<TransactionPersistenceProcessor> _logger;

    public TransactionPersistenceProcessor(
        ITransactionRepository transactionRepository,
        IMessageBusPublisher messageBusPublisher,
        ILogger<TransactionPersistenceProcessor> logger)
    {
        _transactionRepository = transactionRepository;
        _messageBusPublisher = messageBusPublisher;
        _logger = logger;
    }

    public async Task<Result> ProcessAsync(
        CreateTransactionCommandMessage message,
        CancellationToken cancellationToken = default)
    {
        var transactionResult = Transaction.Create(
            message.TransactionId,
            message.Amount,
            message.Type,
            message.Date,
            message.Description,
            message.RequestedAtUtc);

        if (transactionResult.IsFailure)
        {
            _logger.LogWarning(
                "Mensagem de lançamento {TransactionId} inválida ignorada: {Error}",
                message.TransactionId,
                transactionResult.Error.Message);
            return Result.Failure(transactionResult.Error);
        }

        var inserted = await _transactionRepository.TryAddIdempotentAsync(
            transactionResult.Value,
            cancellationToken);

        if (inserted)
        {
            _logger.LogInformation(
                "Lançamento {TransactionId} persistido no banco de dados com sucesso.",
                message.TransactionId);
        }
        else
        {
            _logger.LogInformation(
                "Lançamento {TransactionId} já existia no banco (idempotência aplicada). Garantindo envio para fila de consolidação.",
                message.TransactionId);
        }

        // Dispara o gatilho para a Fila 2 (consolidation-queue) para o Worker 2 consolidar o saldo
        var recordedEvent = new TransactionRecordedEventMessage(
            transactionResult.Value.Id,
            transactionResult.Value.Amount,
            transactionResult.Value.Type,
            transactionResult.Value.Date,
            DateTimeOffset.UtcNow);

        await _messageBusPublisher.PublishTransactionRecordedAsync(recordedEvent, cancellationToken);

        _logger.LogInformation(
            "Evento de consolidação do lançamento {TransactionId} publicado na Fila 2 ({Queue}).",
            message.TransactionId,
            QueueNames.ConsolidationQueue);

        return Result.Success();
    }
}
