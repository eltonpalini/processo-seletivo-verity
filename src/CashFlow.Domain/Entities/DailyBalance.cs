using CashFlow.Domain.Common;
using CashFlow.Domain.Enums;

namespace CashFlow.Domain.Entities;

public sealed class DailyBalance
{
    private DailyBalance() { }

    private DailyBalance(DateOnly date, DateTimeOffset createdAtUtc)
    {
        Date = date;
        TotalCredits = 0m;
        TotalDebits = 0m;
        NetBalance = 0m;
        TransactionCount = 0;
        LastUpdatedAtUtc = createdAtUtc;
    }

    public DateOnly Date { get; private set; }
    public decimal TotalCredits { get; private set; }
    public decimal TotalDebits { get; private set; }
    public decimal NetBalance { get; private set; }
    public int TransactionCount { get; private set; }
    public DateTimeOffset LastUpdatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public static DailyBalance CreateForDate(DateOnly date, DateTimeOffset? createdAtUtc = null)
    {
        if (date == default)
            throw new ArgumentException("A data do saldo consolidado é obrigatória.", nameof(date));

        return new DailyBalance(date, createdAtUtc ?? DateTimeOffset.UtcNow);
    }

    public Result ApplyTransaction(decimal amount, TransactionType type, DateTimeOffset? occurredAtUtc = null)
    {
        if (amount <= 0)
            return Result.Failure("DailyBalance.InvalidAmount", "O valor a ser consolidado deve ser maior que zero.");

        if (!Enum.IsDefined(typeof(TransactionType), type))
            return Result.Failure("DailyBalance.InvalidType", "Tipo de lançamento inválido para consolidação.");

        var normalizedAmount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (type == TransactionType.Credit)
        {
            TotalCredits += normalizedAmount;
        }
        else
        {
            TotalDebits += normalizedAmount;
        }

        NetBalance = TotalCredits - TotalDebits;
        TransactionCount++;
        LastUpdatedAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow;

        return Result.Success();
    }
}
