using CashFlow.Domain.Enums;

namespace CashFlow.Application.Messages;

/// <summary>
/// Mensagem publicada pela Web API na Fila 1 (transactions-queue) e consumida pelo Worker 1 (TransactionWorker).
/// </summary>
public sealed record CreateTransactionCommandMessage(
    Guid TransactionId,
    decimal Amount,
    TransactionType Type,
    DateOnly Date,
    string Description,
    DateTimeOffset RequestedAtUtc);
