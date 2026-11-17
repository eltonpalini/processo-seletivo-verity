using CashFlow.Application;
using CashFlow.Application.Messages;
using CashFlow.Infrastructure;
using CashFlow.Infrastructure.Persistence;
using CashFlow.TransactionWorker.Consumers;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructurePersistence(builder.Configuration)
    .AddInfrastructureCaching(builder.Configuration)
    .AddInfrastructureMessaging(
        builder.Configuration,
        configureConsumers: bus =>
        {
            bus.AddConsumer<CreateTransactionCommandConsumer>();
        },
        configureEndpoints: (context, cfg) =>
        {
            cfg.ReceiveEndpoint(QueueNames.TransactionsQueue, endpoint =>
            {
                endpoint.Durable = true;
                endpoint.PrefetchCount = 32;
                endpoint.UseMessageRetry(retry =>
                    retry.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromMilliseconds(200),
                        maxInterval: TimeSpan.FromSeconds(10),
                        intervalDelta: TimeSpan.FromMilliseconds(300)));

                endpoint.ConfigureConsumer<CreateTransactionCommandConsumer>(context);
            });
        });

var host = builder.Build();

await DatabaseInitializer.InitializeDatabaseAsync(host.Services);

await host.RunAsync();
