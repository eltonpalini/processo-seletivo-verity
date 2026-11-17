using CashFlow.Application.Messages;
using CashFlow.Application.Services;
using MassTransit;

namespace CashFlow.TransactionWorker.Consumers;

public sealed class CreateTransactionCommandConsumer : IConsumer<CreateTransactionCommandMessage>
{
    private readonly ITransactionPersistenceProcessor _processor;

    public CreateTransactionCommandConsumer(ITransactionPersistenceProcessor processor)
    {
        _processor = processor;
    }

    public async Task Consume(ConsumeContext<CreateTransactionCommandMessage> context)
    {
        var result = await _processor.ProcessAsync(context.Message, context.CancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Erro ao processar gravação do lançamento {context.Message.TransactionId}: {result.Error.Message}");
        }
    }
}
