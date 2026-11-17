namespace CashFlow.Domain.Entities;

/// <summary>
/// Registro de controle de idempotência para garantir que um mesmo lançamento (TransactionId)
/// jamais seja somado mais de uma vez no saldo diário consolidado em caso de reentrega de mensagens.
/// </summary>
public sealed class ProcessedTransaction
{
    private ProcessedTransaction() { }

    public ProcessedTransaction(Guid transactionId, DateOnly date, DateTimeOffset consolidatedAtUtc)
    {
        TransactionId = transactionId;
        Date = date;
        ConsolidatedAtUtc = consolidatedAtUtc;
    }

    public Guid TransactionId { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTimeOffset ConsolidatedAtUtc { get; private set; }
}
