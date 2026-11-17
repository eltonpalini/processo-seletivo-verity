using CashFlow.Application.Abstractions;
using CashFlow.Application.DTOs;
using CashFlow.Application.Messages;
using CashFlow.Domain.Common;
using CashFlow.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CashFlow.Application.Services;

public interface ITransactionIngestionService
{
    Task<Result<TransactionAcceptedResponse>> EnqueueAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TransactionIngestionService : ITransactionIngestionService
{
    private readonly IValidator<CreateTransactionRequest> _validator;
    private readonly IMessageBusPublisher _messageBusPublisher;
    private readonly ILogger<TransactionIngestionService> _logger;

    public TransactionIngestionService(
        IValidator<CreateTransactionRequest> validator,
        IMessageBusPublisher messageBusPublisher,
        ILogger<TransactionIngestionService> logger)
    {
        _validator = validator;
        _messageBusPublisher = messageBusPublisher;
        _logger = logger;
    }

    public async Task<Result<TransactionAcceptedResponse>> EnqueueAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var firstError = validationResult.Errors[0];
            return Result.Failure<TransactionAcceptedResponse>("Validation.Error", firstError.ErrorMessage);
        }

        var transactionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Valida também as invariantes da entidade de domínio antes de publicar na fila
        var domainCheck = Transaction.Create(
            transactionId,
            request.Amount,
            request.Type,
            request.Date,
            request.Description,
            now);

        if (domainCheck.IsFailure)
        {
            return Result.Failure<TransactionAcceptedResponse>(domainCheck.Error);
        }

        var commandMessage = new CreateTransactionCommandMessage(
            domainCheck.Value.Id,
            domainCheck.Value.Amount,
            domainCheck.Value.Type,
            domainCheck.Value.Date,
            domainCheck.Value.Description,
            now);

        await _messageBusPublisher.EnqueueTransactionAsync(commandMessage, cancellationToken);

        _logger.LogInformation(
            "Lançamento {TransactionId} ({Type} - {Amount:C2} em {Date}) validado e enfileirado na Fila 1 ({Queue}).",
            commandMessage.TransactionId,
            commandMessage.Type,
            commandMessage.Amount,
            commandMessage.Date,
            QueueNames.TransactionsQueue);

        return Result.Success(new TransactionAcceptedResponse(
            transactionId,
            "Accepted",
            now,
            "Lançamento recebido e enviado para processamento assíncrono."));
    }
}
