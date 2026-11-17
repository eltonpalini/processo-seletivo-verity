using CashFlow.Application.Services;
using CashFlow.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateTransactionRequestValidator>();

        services.AddScoped<ITransactionIngestionService, TransactionIngestionService>();
        services.AddScoped<ITransactionPersistenceProcessor, TransactionPersistenceProcessor>();
        services.AddScoped<IDailyBalanceConsolidationProcessor, DailyBalanceConsolidationProcessor>();
        services.AddScoped<IDailyBalanceQueryService, DailyBalanceQueryService>();

        return services;
    }
}
