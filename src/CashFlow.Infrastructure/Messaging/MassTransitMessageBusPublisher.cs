using CashFlow.Application.Abstractions;
using CashFlow.Application.Messages;
using MassTransit;

namespace CashFlow.Infrastructure.Messaging;

public sealed class MassTransitMessageBusPublisher : IMessageBusPublisher
{
    private readonly IBus _bus;

    public MassTransitMessageBusPublisher(IBus bus)
    {
        _bus = bus;
    }

    public async Task EnqueueTransactionAsync(
        CreateTransactionCommandMessage message,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _bus.GetSendEndpoint(
            new Uri($"queue:{QueueNames.TransactionsQueue}"));

        await endpoint.Send(message, context =>
        {
            context.Durable = true;
            context.CorrelationId = message.TransactionId;
        }, cancellationToken);
    }

    public async Task PublishTransactionRecordedAsync(
        TransactionRecordedEventMessage message,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _bus.GetSendEndpoint(
            new Uri($"queue:{QueueNames.ConsolidationQueue}"));

        await endpoint.Send(message, context =>
        {
            context.Durable = true;
            context.CorrelationId = message.TransactionId;
        }, cancellationToken);
    }
}
