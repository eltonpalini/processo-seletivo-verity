using CashFlow.Application;
using CashFlow.Application.Messages;
using CashFlow.ConsolidationWorker.Consumers;
using CashFlow.Infrastructure;
using CashFlow.Infrastructure.Persistence;
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
            bus.AddConsumer<TransactionRecordedEventConsumer>();
        },
        configureEndpoints: (context, cfg) =>
        {
            // Concorrência controlada por data/partição e retry exponencial para resiliência a locks otimistas
            cfg.ReceiveEndpoint(QueueNames.ConsolidationQueue, endpoint =>
            {
                endpoint.Durable = true;
                endpoint.PrefetchCount = 16;
                endpoint.ConcurrentMessageLimit = 4;
                endpoint.UseMessageRetry(retry =>
                    retry.Exponential(
                        retryLimit: 5,
                        minInterval: TimeSpan.FromMilliseconds(200),
                        maxInterval: TimeSpan.FromSeconds(10),
                        intervalDelta: TimeSpan.FromMilliseconds(300)));

                endpoint.ConfigureConsumer<TransactionRecordedEventConsumer>(context);
            });
        });

var host = builder.Build();

await DatabaseInitializer.InitializeDatabaseAsync(host.Services);

await host.RunAsync();
