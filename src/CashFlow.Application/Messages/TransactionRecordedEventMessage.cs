using CashFlow.Domain.Enums;

namespace CashFlow.Application.Messages;

/// <summary>
/// Evento publicado pelo Worker 1 (TransactionWorker) na Fila 2 (consolidation-queue)
/// após a gravação do lançamento no banco de dados, e consumido pelo Worker 2 (ConsolidationWorker).
/// </summary>
public sealed record TransactionRecordedEventMessage(
    Guid TransactionId,
    decimal Amount,
    TransactionType Type,
    DateOnly Date,
    DateTimeOffset RecordedAtUtc);
