using CashFlow.Domain.Entities;
using CashFlow.Domain.Enums;
using FluentAssertions;

namespace CashFlow.UnitTests.Domain;

public sealed class DailyBalanceTests
{
    [Fact]
    public void ApplyTransaction_WithMultipleCreditsAndDebits_ShouldComputeNetBalanceCorrectly()
    {
        // Arrange
        var date = new DateOnly(2026, 11, 16);
        var balance = DailyBalance.CreateForDate(date);

        // Act
        var r1 = balance.ApplyTransaction(1000.00m, TransactionType.Credit);
        var r2 = balance.ApplyTransaction(250.50m, TransactionType.Credit);
        var r3 = balance.ApplyTransaction(400.25m, TransactionType.Debit);

        // Assert
        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        r3.IsSuccess.Should().BeTrue();

        balance.TotalCredits.Should().Be(1250.50m);
        balance.TotalDebits.Should().Be(400.25m);
        balance.NetBalance.Should().Be(850.25m);
        balance.TransactionCount.Should().Be(3);
    }

    [Fact]
    public void ApplyTransaction_WhenDebitsExceedCredits_ShouldAllowNegativeNetBalance()
    {
        // Arrange
        var balance = DailyBalance.CreateForDate(new DateOnly(2026, 11, 16));

        // Act
        balance.ApplyTransaction(100.00m, TransactionType.Credit);
        balance.ApplyTransaction(350.00m, TransactionType.Debit);

        // Assert
        balance.NetBalance.Should().Be(-250.00m);
        balance.TransactionCount.Should().Be(2);
    }

    [Fact]
    public void ApplyTransaction_WithInvalidAmount_ShouldReturnFailure()
    {
        var balance = DailyBalance.CreateForDate(new DateOnly(2026, 11, 16));

        var result = balance.ApplyTransaction(0m, TransactionType.Credit);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DailyBalance.InvalidAmount");
    }
}
