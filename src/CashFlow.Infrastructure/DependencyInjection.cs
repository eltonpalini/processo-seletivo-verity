using CashFlow.Application.Abstractions;
using CashFlow.Domain.Interfaces;
using CashFlow.Infrastructure.Caching;
using CashFlow.Infrastructure.Messaging;
using CashFlow.Infrastructure.Persistence;
using CashFlow.Infrastructure.Persistence.Repositories;
using CashFlow.Infrastructure.Security;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructurePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=cashflow_db;Username=postgres;Password=postgres";

        services.AddDbContext<CashFlowDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IDailyBalanceRepository, DailyBalanceRepository>();

        return services;
    }

    public static IServiceCollection AddInfrastructureCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddScoped<IDailyBalanceCacheService, RedisDailyBalanceCacheService>();

        return services;
    }

    public static IServiceCollection AddInfrastructureMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null)
    {
        services.AddScoped<IMessageBusPublisher, MassTransitMessageBusPublisher>();

        services.AddMassTransit(busConfigurator =>
        {
            configureConsumers?.Invoke(busConfigurator);

            busConfigurator.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration["RabbitMq:Host"] ?? "localhost";
                var port = ushort.TryParse(configuration["RabbitMq:Port"], out var parsedPort) ? parsedPort : (ushort)5672;
                var username = configuration["RabbitMq:Username"] ?? "guest";
                var password = configuration["RabbitMq:Password"] ?? "guest";
                var virtualHost = configuration["RabbitMq:VirtualHost"] ?? "/";

                cfg.Host(host, port, virtualHost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                configureEndpoints?.Invoke(context, cfg);
            });
        });

        return services;
    }

    public static IServiceCollection AddInfrastructureSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
