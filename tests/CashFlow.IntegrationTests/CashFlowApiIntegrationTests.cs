using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Application.Messages;
using CashFlow.Application.Services;
using CashFlow.Domain.Enums;
using CashFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CashFlow.IntegrationTests;

/// <summary>
/// Barramento em memória para testes de integração que simula fielmente a Fila 1 (transactions-queue)
/// e a Fila 2 (consolidation-queue), permitindo inclusive simular a queda do Worker 2 (ConsolidationWorker).
/// </summary>
public sealed class InMemoryQueueBroker : IMessageBusPublisher
{
    public ConcurrentQueue<CreateTransactionCommandMessage> TransactionsQueue { get; } = new();
    public ConcurrentQueue<TransactionRecordedEventMessage> ConsolidationQueue { get; } = new();

    public Task EnqueueTransactionAsync(CreateTransactionCommandMessage message, CancellationToken cancellationToken = default)
    {
        TransactionsQueue.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishTransactionRecordedAsync(TransactionRecordedEventMessage message, CancellationToken cancellationToken = default)
    {
        ConsolidationQueue.Enqueue(message);
        return Task.CompletedTask;
    }
}

public sealed class CashFlowApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"CashFlowIntegrationDb_{Guid.NewGuid()}";
    public InMemoryQueueBroker Broker { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:AutoInitialize", "false");
        builder.UseSetting("RateLimiting:ApiPermitLimit", "500");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CashFlowDbContext>>();
            services.AddDbContext<CashFlowDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.RemoveAll<IMessageBusPublisher>();
            services.AddSingleton<IMessageBusPublisher>(Broker);

            services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }

    /// <summary>
    /// Simula a execução do Worker 1 (TransactionWorker) drenando a Fila 1 (transactions-queue).
    /// </summary>
    public async Task DrainTransactionsQueueAsync()
    {
        using var scope = Services.CreateScope();
        var worker1 = scope.ServiceProvider.GetRequiredService<ITransactionPersistenceProcessor>();

        while (Broker.TransactionsQueue.TryDequeue(out var msg))
        {
            await worker1.ProcessAsync(msg);
        }
    }

    /// <summary>
    /// Simula a execução do Worker 2 (ConsolidationWorker) drenando a Fila 2 (consolidation-queue).
    /// </summary>
    public async Task DrainConsolidationQueueAsync()
    {
        using var scope = Services.CreateScope();
        var worker2 = scope.ServiceProvider.GetRequiredService<IDailyBalanceConsolidationProcessor>();

        while (Broker.ConsolidationQueue.TryDequeue(out var evt))
        {
            await worker2.ProcessAsync(evt);
        }
    }
}

public sealed class CashFlowApiIntegrationTests : IClassFixture<CashFlowApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CashFlowApiFactory _factory;

    public CashFlowApiIntegrationTests(CashFlowApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new TokenRequest("comerciante", "Verity@2026"));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Endpoints_WithoutJwtToken_ShouldReturn401Unauthorized()
    {
        var client = _factory.CreateClient();

        var postResponse = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(100m, TransactionType.Credit, new DateOnly(2026, 11, 16), "Teste"));

        var getResponse = await client.GetAsync("/api/v1/daily-balance");

        postResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDailyBalance_WithoutDateParameter_ShouldAssumeCurrentDate()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);

        await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(450.00m, TransactionType.Credit, today, "Venda do dia atual"));

        await _factory.DrainTransactionsQueueAsync();
        await _factory.DrainConsolidationQueueAsync();

        // Act: chama GET /api/v1/daily-balance sem informar a data
        var response = await client.GetAsync("/api/v1/daily-balance");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var balance = await response.Content.ReadFromJsonAsync<DailyBalanceResponse>(JsonOptions);
        balance.Should().NotBeNull();
        balance!.Date.Should().Be(today);
        balance.NetBalance.Should().Be(450.00m);
    }

    [Fact]
    public async Task EndToEndFlow_WhenTransactionsArePosted_ShouldProcessViaBothWorkersAndReturnConsolidatedBalance()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var targetDate = new DateOnly(2025, 05, 10);

        // Act 1: Envia 3 lançamentos na Web API -> Enfileira na Fila 1 (202 Accepted)
        var r1 = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(1000.00m, TransactionType.Credit, targetDate, "Venda 01"));
        var r2 = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(500.50m, TransactionType.Credit, targetDate, "Venda 02"));
        var r3 = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(250.25m, TransactionType.Debit, targetDate, "Pagamento Energia"));

        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        r2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        r3.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act 2: Worker 1 consome a Fila 1, grava no banco e publica na Fila 2
        await _factory.DrainTransactionsQueueAsync();

        // Act 3: Worker 2 consome a Fila 2, consolida o saldo no banco e atualiza o cache
        await _factory.DrainConsolidationQueueAsync();

        // Act 4: Consulta o saldo diário consolidado informando a data via query string
        var balanceResponse = await client.GetAsync($"/api/v1/daily-balance?date={targetDate:yyyy-MM-dd}");
        balanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var balance = await balanceResponse.Content.ReadFromJsonAsync<DailyBalanceResponse>(JsonOptions);

        // Assert
        balance.Should().NotBeNull();
        balance!.Date.Should().Be(targetDate);
        balance.TotalCredits.Should().Be(1500.50m);
        balance.TotalDebits.Should().Be(250.25m);
        balance.NetBalance.Should().Be(1250.25m);
        balance.TransactionCount.Should().Be(3);
        balance.Source.Should().Be("RedisCache");
    }

    [Fact]
    public async Task ResilienceTest_WhenConsolidationWorkerIsDown_TransactionsServiceRemains100PercentAvailableAndConsolidatesWhenRecovered()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var date = new DateOnly(2025, 05, 11);

        // Act 1: Worker 2 (Consolidação) está CAÍDO (não drenamos a Fila 2).
        // Mesmo assim, o comerciante registra lançamentos normalmente e o Worker 1 grava no banco!
        var post1 = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(800.00m, TransactionType.Credit, date, "Venda durante queda do consolidado"));
        var post2 = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(150.00m, TransactionType.Debit, date, "Frete durante queda do consolidado"));

        post1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        post2.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Worker 1 continua operando normalmente e gravando os lançamentos
        await _factory.DrainTransactionsQueueAsync();

        // Verifica que os eventos ficaram retidos na Fila 2 aguardando o Worker 2 voltar
        _factory.Broker.ConsolidationQueue.Count.Should().BeGreaterThanOrEqualTo(2);

        // Act 2: Worker 2 (Consolidação) volta a ficar online e processa a Fila 2 acumulada
        await _factory.DrainConsolidationQueueAsync();

        var balance = await client.GetFromJsonAsync<DailyBalanceResponse>(
            $"/api/v1/daily-balance?date={date:yyyy-MM-dd}",
            JsonOptions);

        // Assert: Zero perda de lançamentos!
        balance.Should().NotBeNull();
        balance!.NetBalance.Should().Be(650.00m);
        balance.TransactionCount.Should().Be(2);
    }

    [Fact]
    public async Task PeakLoadTest_WhenDailyBalanceReceivesOver50RequestsPerSecond_ShouldCompleteWithZeroPercentLoss()
    {
        // Arrange: prepara saldo pré-computado para o dia 2025-05-12
        var client = await CreateAuthenticatedClientAsync();
        var date = new DateOnly(2025, 05, 12);

        await client.PostAsJsonAsync(
            "/api/v1/transactions",
            new CreateTransactionRequest(2000.00m, TransactionType.Credit, date, "Saldo Inicial Pico"));
        await _factory.DrainTransactionsQueueAsync();
        await _factory.DrainConsolidationQueueAsync();

        // Act: Dispara 100 requisições simultâneas (2x o pico exigido de 50 req/s)
        const int totalRequests = 100;
        var tasks = Enumerable.Range(0, totalRequests)
            .Select(_ => client.GetAsync($"/api/v1/daily-balance?date={date:yyyy-MM-dd}"));

        var responses = await Task.WhenAll(tasks);

        // Assert: 0% de perda de requisições (requisito exige no máximo 5% de perda)
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var lossPercentage = ((totalRequests - successCount) / (double)totalRequests) * 100.0;

        lossPercentage.Should().Be(0, "o requisito exige no máximo 5% de perda em picos de 50 req/s");
    }
}
