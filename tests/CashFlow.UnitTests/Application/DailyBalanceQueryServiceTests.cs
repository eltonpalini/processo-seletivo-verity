using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Application.Services;
using CashFlow.Domain.Entities;
using CashFlow.Domain.Enums;
using CashFlow.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using CashFlow.Infrastructure.Caching;

namespace CashFlow.UnitTests.Application;

public sealed class DailyBalanceQueryServiceTests
{
    [Fact]
    public async Task GetByDateAsync_WhenCacheHit_ShouldReturnFromRedisWithoutQueryingDatabase()
    {
        // Arrange
        var date = new DateOnly(2026, 11, 16);
        var cachedResponse = new DailyBalanceResponse(
            date,
            1000m,
            300m,
            700m,
            5,
            DateTimeOffset.UtcNow,
            "RedisCache");

        var cacheService = Substitute.For<IDailyBalanceCacheService>();
        cacheService.GetAsync(date, Arg.Any<CancellationToken>()).Returns(cachedResponse);

        var repository = Substitute.For<IDailyBalanceRepository>();
        var sut = new DailyBalanceQueryService(cacheService, repository, NullLogger<DailyBalanceQueryService>.Instance);

        // Act
        var result = await sut.GetByDateAsync(date);

        // Assert
        result.NetBalance.Should().Be(700m);
        result.Source.Should().Be("RedisCache");
        await repository.DidNotReceiveWithAnyArgs().GetByDateAsync(default);
    }

    [Fact]
    public async Task GetByDateAsync_WhenRedisThrowsException_ShouldGracefullyFallbackToDatabase()
    {
        // Arrange
        var date = new DateOnly(2026, 11, 16);
        var brokenDistributedCache = Substitute.For<IDistributedCache>();
        brokenDistributedCache
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Redis connection timeout"));

        var redisCacheService = new RedisDailyBalanceCacheService(
            brokenDistributedCache,
            NullLogger<RedisDailyBalanceCacheService>.Instance);

        var dbEntity = DailyBalance.CreateForDate(date);
        dbEntity.ApplyTransaction(900m, TransactionType.Credit);
        dbEntity.ApplyTransaction(150m, TransactionType.Debit);

        var repository = Substitute.For<IDailyBalanceRepository>();
        repository.GetByDateAsync(date, Arg.Any<CancellationToken>()).Returns(dbEntity);

        var sut = new DailyBalanceQueryService(redisCacheService, repository, NullLogger<DailyBalanceQueryService>.Instance);

        // Act
        var result = await sut.GetByDateAsync(date);

        // Assert
        result.Should().NotBeNull();
        result.NetBalance.Should().Be(750m);
        result.TotalCredits.Should().Be(900m);
        result.TotalDebits.Should().Be(150m);
        result.TransactionCount.Should().Be(2);
        result.Source.Should().Be("Database");
    }
}
