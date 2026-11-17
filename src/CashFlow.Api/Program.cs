using CashFlow.Api;
using CashFlow.Api.Endpoints;
using CashFlow.Application;
using CashFlow.Infrastructure;
using CashFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructurePersistence(builder.Configuration)
    .AddInfrastructureCaching(builder.Configuration)
    .AddInfrastructureMessaging(builder.Configuration)
    .AddInfrastructureSecurity(builder.Configuration);

new Bootstrapper().Configure(builder.Services, builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue("Database:AutoInitialize", true))
{
    await DatabaseInitializer.InitializeDatabaseAsync(app.Services);
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "CashFlow API v1");
    options.RoutePrefix = "swagger";
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

RouteGroupBuilder apiV1 = app.MapGroup("/api/v1");
apiV1.MapCashFlowEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

await app.RunAsync();

public partial class Program;
