using CashFlow.Domain.Common;
using CashFlow.Domain.Enums;

namespace CashFlow.Domain.Entities;

public sealed class Transaction
{
    public const int MaxDescriptionLength = 200;

    private Transaction() { }

    private Transaction(
        Guid id,
        decimal amount,
        TransactionType type,
        DateOnly date,
        string description,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Type = type;
        Date = date;
        Description = description.Trim();
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public decimal Amount { get; private set; }
    public TransactionType Type { get; private set; }
    public DateOnly Date { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Result<Transaction> Create(
        Guid id,
        decimal amount,
        TransactionType type,
        DateOnly date,
        string description,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty)
            return Result.Failure<Transaction>("Transaction.InvalidId", "O identificador do lançamento é obrigatório.");

        if (amount <= 0)
            return Result.Failure<Transaction>("Transaction.InvalidAmount", "O valor do lançamento deve ser maior que zero.");

        if (!Enum.IsDefined(typeof(TransactionType), type))
            return Result.Failure<Transaction>("Transaction.InvalidType", "O tipo de lançamento deve ser Credit (1) ou Debit (2).");

        if (date == default)
            return Result.Failure<Transaction>("Transaction.InvalidDate", "A data do lançamento é obrigatória.");

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<Transaction>("Transaction.EmptyDescription", "A descrição do lançamento é obrigatória.");

        if (description.Trim().Length > MaxDescriptionLength)
            return Result.Failure<Transaction>("Transaction.DescriptionTooLong", $"A descrição não pode exceder {MaxDescriptionLength} caracteres.");

        var timestamp = createdAtUtc ?? DateTimeOffset.UtcNow;

        return Result.Success(new Transaction(id, amount, type, date, description, timestamp));
    }
}
