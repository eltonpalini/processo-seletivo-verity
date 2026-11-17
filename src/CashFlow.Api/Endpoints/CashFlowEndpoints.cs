using CashFlow.Application.DTOs;
using CashFlow.Application.Services;
using CashFlow.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Api.Endpoints;

public static class CashFlowEndpoints
{
    public static void MapCashFlowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/token", GenerateToken)
            .WithName("GenerateToken")
            .WithTags("Autenticação")
            .WithSummary("Gera um token JWT (Credenciais padrão: comerciante / Verity@2026)")
            .RequireRateLimiting("AuthPolicy")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        app.MapPost("/transactions", CreateTransaction)
            .WithName("CreateTransaction")
            .WithTags("Controle de Lançamentos")
            .WithSummary("Valida e enfileira um novo lançamento financeiro (Crédito ou Débito)")
            .RequireAuthorization("CanWriteCashFlow")
            .RequireRateLimiting("ApiPolicy")
            .Produces<TransactionAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        app.MapGet("/daily-balance", GetDailyBalance)
            .WithName("GetDailyBalance")
            .WithTags("Consolidado Diário")
            .WithSummary("Retorna o saldo diário consolidado para a data informada (YYYY-MM-DD) ou para a data atual caso não seja informada")
            .RequireAuthorization("CanReadCashFlow")
            .RequireRateLimiting("ApiPolicy")
            .Produces<DailyBalanceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        app.MapGet("/daily-balance/{date}", (DateOnly date, IDailyBalanceQueryService queryService, CancellationToken cancellationToken) =>
                GetDailyBalance(date, queryService, cancellationToken))
            .RequireAuthorization("CanReadCashFlow")
            .RequireRateLimiting("ApiPolicy")
            .ExcludeFromDescription();
    }

    private static IResult GenerateToken(
        [FromBody] TokenRequest request,
        IJwtTokenService jwtTokenService)
    {
        var result = jwtTokenService.GenerateToken(request);
        if (result.IsFailure)
        {
            return Results.Problem(
                title: "Falha de autenticação",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateTransaction(
        [FromBody] CreateTransactionRequest request,
        ITransactionIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var result = await ingestionService.EnqueueAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return Results.Problem(
                title: "Lançamento inválido",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Accepted(
            uri: $"/api/v1/daily-balance?date={request.Date:yyyy-MM-dd}",
            value: result.Value);
    }

    private static async Task<IResult> GetDailyBalance(
        [FromQuery] DateOnly? date,
        IDailyBalanceQueryService queryService,
        CancellationToken cancellationToken)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await queryService.GetByDateAsync(targetDate, cancellationToken);
        return Results.Ok(response);
    }
}
