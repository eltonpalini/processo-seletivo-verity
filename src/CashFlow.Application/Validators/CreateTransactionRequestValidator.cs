using CashFlow.Application.DTOs;
using CashFlow.Domain.Entities;
using FluentValidation;

namespace CashFlow.Application.Validators;

public sealed class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("O valor do lançamento deve ser maior que zero.");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("O tipo de lançamento deve ser 'Credit' (1) ou 'Debit' (2).");

        RuleFor(x => x.Date)
            .NotEmpty()
            .WithMessage("A data do lançamento é obrigatória.");

        RuleFor(x => x.Description)
            .NotEmpty()
            .WithMessage("A descrição do lançamento é obrigatória.")
            .MaximumLength(Transaction.MaxDescriptionLength)
            .WithMessage($"A descrição não pode exceder {Transaction.MaxDescriptionLength} caracteres.");
    }
}
