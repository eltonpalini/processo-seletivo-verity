using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Application.Messages;
using CashFlow.Application.Services;
using CashFlow.Application.Validators;
using CashFlow.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CashFlow.UnitTests.Application;

public sealed class TransactionIngestionServiceTests
{
    private readonly IMessageBusPublisher _publisher = Substitute.For<IMessageBusPublisher>();
    private readonly TransactionIngestionService _sut;

    public TransactionIngestionServiceTests()
    {
        var validator = new CreateTransactionRequestValidator();
        _sut = new TransactionIngestionService(validator, _publisher, NullLogger<TransactionIngestionService>.Instance);
    }

    [Fact]
    public async Task EnqueueAsync_WithValidRequest_ShouldPublishToTransactionsQueueAndReturnAccepted()
    {
        // Arrange
        var request = new CreateTransactionRequest(
            Amount: 320.75m,
            Type: TransactionType.Credit,
            Date: new DateOnly(2026, 11, 16),
            Description: "Recebimento PIX");

        // Act
        var result = await _sut.EnqueueAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionId.Should().NotBeEmpty();
        result.Value.Status.Should().Be("Accepted");

        await _publisher.Received(1).EnqueueTransactionAsync(
            Arg.Is<CreateTransactionCommandMessage>(msg =>
                msg.TransactionId == result.Value.TransactionId &&
                msg.Amount == 320.75m &&
                msg.Type == TransactionType.Credit &&
                msg.Date == request.Date &&
                msg.Description == "Recebimento PIX"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueAsync_WithInvalidRequest_ShouldNotPublishToQueueAndReturnFailure()
    {
        // Arrange
        var invalidRequest = new CreateTransactionRequest(
            Amount: -50m,
            Type: TransactionType.Debit,
            Date: new DateOnly(2026, 11, 16),
            Description: "");

        // Act
        var result = await _sut.EnqueueAsync(invalidRequest);

        // Assert
        result.IsFailure.Should().BeTrue();
        await _publisher.DidNotReceiveWithAnyArgs().EnqueueTransactionAsync(default!);
    }
}
