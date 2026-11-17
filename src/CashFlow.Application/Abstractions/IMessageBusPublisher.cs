using CashFlow.Application.Messages;

namespace CashFlow.Application.Abstractions;

public interface IMessageBusPublisher
{
    /// <summary>
    /// Enfileira o lançamento validado na Fila 1 (transactions-queue) para gravação pelo TransactionWorker.
    /// </summary>
    Task EnqueueTransactionAsync(CreateTransactionCommandMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica o gatilho na Fila 2 (consolidation-queue) após a gravação do lançamento para consolidação pelo ConsolidationWorker.
    /// </summary>
    Task PublishTransactionRecordedAsync(TransactionRecordedEventMessage message, CancellationToken cancellationToken = default);
}
