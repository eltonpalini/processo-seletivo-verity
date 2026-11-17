using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CashFlow.Application.DTOs;
using CashFlow.Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CashFlow.Infrastructure.Security;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "CashFlow.Identity";
    public string Audience { get; init; } = "CashFlow.Api";
    public string SecretKey { get; init; } = "SuperSecretKeyForCashFlowArchitectureChallenge2026!@#";
    public int ExpirationMinutes { get; init; } = 60;
    public string DemoUsername { get; init; } = "comerciante";
    public string DemoPassword { get; init; } = "Verity@2026";
}

public interface IJwtTokenService
{
    Result<TokenResponse> GenerateToken(TokenRequest request);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    public Result<TokenResponse> GenerateToken(TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<TokenResponse>("Auth.InvalidCredentials", "Usuário e senha são obrigatórios.");
        }

        if (!string.Equals(request.Username, _settings.DemoUsername, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Password, _settings.DemoPassword, StringComparison.Ordinal))
        {
            return Result.Failure<TokenResponse>("Auth.Unauthorized", "Credenciais inválidas.");
        }

        var roles = new[] { "Merchant" };
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Username),
            new(JwtRegisteredClaimNames.UniqueName, request.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, "Merchant"),
            new("scope", "cashflow.write"),
            new("scope", "cashflow.read")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresInSeconds = _settings.ExpirationMinutes * 60;
        var expires = DateTime.UtcNow.AddSeconds(expiresInSeconds);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Result.Success(new TokenResponse(
            AccessToken: tokenString,
            TokenType: "Bearer",
            ExpiresInSeconds: expiresInSeconds,
            Roles: roles));
    }
}
