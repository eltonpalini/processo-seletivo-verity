using CashFlow.Domain.Entities;
using CashFlow.Domain.Enums;
using FluentAssertions;

namespace CashFlow.UnitTests.Domain;

public sealed class TransactionTests
{
    [Fact]
    public void Create_WithValidData_ShouldReturnSuccessAndNormalizeValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 16);

        // Act
        var result = Transaction.Create(id, 150.556m, TransactionType.Credit, date, "  Venda de mercadoria  ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.Amount.Should().Be(150.56m);
        result.Value.Type.Should().Be(TransactionType.Credit);
        result.Value.Date.Should().Be(date);
        result.Value.Description.Should().Be("Venda de mercadoria");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10.50)]
    public void Create_WithNonPositiveAmount_ShouldReturnFailure(decimal invalidAmount)
    {
        // Act
        var result = Transaction.Create(
            Guid.NewGuid(),
            invalidAmount,
            TransactionType.Debit,
            new DateOnly(2026, 11, 16),
            "Pagamento fornecedor");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transaction.InvalidAmount");
    }

    [Fact]
    public void Create_WithEmptyGuid_ShouldReturnFailure()
    {
        var result = Transaction.Create(
            Guid.Empty,
            100m,
            TransactionType.Credit,
            new DateOnly(2026, 11, 16),
            "Venda");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transaction.InvalidId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyDescription_ShouldReturnFailure(string description)
    {
        var result = Transaction.Create(
            Guid.NewGuid(),
            100m,
            TransactionType.Credit,
            new DateOnly(2026, 11, 16),
            description);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transaction.EmptyDescription");
    }

    [Fact]
    public void Create_WithDescriptionExceedingMaxLength_ShouldReturnFailure()
    {
        var longDescription = new string('A', Transaction.MaxDescriptionLength + 1);

        var result = Transaction.Create(
            Guid.NewGuid(),
            100m,
            TransactionType.Credit,
            new DateOnly(2026, 11, 16),
            longDescription);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transaction.DescriptionTooLong");
    }
}
