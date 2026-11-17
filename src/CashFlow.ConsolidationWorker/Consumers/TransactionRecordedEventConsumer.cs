using CashFlow.Application.Messages;
using CashFlow.Application.Services;
using MassTransit;

namespace CashFlow.ConsolidationWorker.Consumers;

public sealed class TransactionRecordedEventConsumer : IConsumer<TransactionRecordedEventMessage>
{
    private readonly IDailyBalanceConsolidationProcessor _processor;

    public TransactionRecordedEventConsumer(IDailyBalanceConsolidationProcessor processor)
    {
        _processor = processor;
    }

    public async Task Consume(ConsumeContext<TransactionRecordedEventMessage> context)
    {
        var result = await _processor.ProcessAsync(context.Message, context.CancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Erro ao consolidar saldo do lançamento {context.Message.TransactionId}: {result.Error.Message}");
        }
    }
}
