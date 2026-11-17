using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Application.Messages;
using CashFlow.Application.Services;
using CashFlow.Domain.Enums;
using CashFlow.Infrastructure.Persistence;
using CashFlow.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CashFlow.UnitTests.Application;

public sealed class WorkerProcessorsIdempotencyTests
{
    private static CashFlowDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<CashFlowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CashFlowDbContext(options);
    }

    [Fact]
    public async Task TransactionPersistenceProcessor_WhenDuplicateMessageArrives_ShouldPersistOnceAndTriggerConsolidation()
    {
        // Arrange
        await using var dbContext = CreateInMemoryDbContext();
        var repository = new TransactionRepository(dbContext, NullLogger<TransactionRepository>.Instance);
        var publisher = Substitute.For<IMessageBusPublisher>();
        var processor = new TransactionPersistenceProcessor(
            repository,
            publisher,
            NullLogger<TransactionPersistenceProcessor>.Instance);

        var message = new CreateTransactionCommandMessage(
            Guid.NewGuid(),
            500.00m,
            TransactionType.Credit,
            new DateOnly(2026, 11, 16),
            "Venda balcão",
            DateTimeOffset.UtcNow);

        // Act - processa a mesma mensagem 2 vezes (simulando reentrega da Fila 1)
        var firstRun = await processor.ProcessAsync(message);
        var secondRun = await processor.ProcessAsync(message);

        // Assert
        firstRun.IsSuccess.Should().BeTrue();
        secondRun.IsSuccess.Should().BeTrue();

        var countInDb = await dbContext.Transactions.CountAsync(x => x.Id == message.TransactionId);
        countInDb.Should().Be(1, "o lançamento não deve ser duplicado no banco de dados");

        await publisher.Received(2).PublishTransactionRecordedAsync(
            Arg.Is<TransactionRecordedEventMessage>(e => e.TransactionId == message.TransactionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DailyBalanceConsolidationProcessor_WhenDuplicateEventArrives_ShouldConsolidateBalanceOnlyOnce()
    {
        // Arrange
        await using var dbContext = CreateInMemoryDbContext();
        var repository = new DailyBalanceRepository(dbContext);
        var cacheService = Substitute.For<IDailyBalanceCacheService>();
        var processor = new DailyBalanceConsolidationProcessor(
            repository,
            cacheService,
            NullLogger<DailyBalanceConsolidationProcessor>.Instance);

        var transactionId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 16);
        var eventMessage = new TransactionRecordedEventMessage(
            transactionId,
            200.00m,
            TransactionType.Credit,
            date,
            DateTimeOffset.UtcNow);

        // Act - entrega o mesmo evento 3 vezes seguidas (simulando reentrega da Fila 2)
        var r1 = await processor.ProcessAsync(eventMessage);
        var r2 = await processor.ProcessAsync(eventMessage);
        var r3 = await processor.ProcessAsync(eventMessage);

        // Assert
        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        r3.IsSuccess.Should().BeTrue();

        var consolidated = await repository.GetByDateAsync(date);
        consolidated.Should().NotBeNull();
        consolidated!.TotalCredits.Should().Be(200.00m, "o valor jamais deve ser somado duplicado");
        consolidated.NetBalance.Should().Be(200.00m);
        consolidated.TransactionCount.Should().Be(1);

        await cacheService.Received().SetAsync(
            Arg.Is<DailyBalanceResponse>(b => b.Date == date && b.NetBalance == 200.00m),
            Arg.Any<CancellationToken>());
    }
}
