namespace CashFlow.Application.DTOs;

public sealed record DailyBalanceResponse(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal NetBalance,
    int TransactionCount,
    DateTimeOffset LastUpdatedAtUtc,
    string Source);
