using CashFlow.Domain.Enums;

namespace CashFlow.Application.DTOs;

public sealed record CreateTransactionRequest(
    decimal Amount,
    TransactionType Type,
    DateOnly Date,
    string Description);

public sealed record TransactionAcceptedResponse(
    Guid TransactionId,
    string Status,
    DateTimeOffset AcceptedAtUtc,
    string Message);
