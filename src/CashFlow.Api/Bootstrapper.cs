using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CashFlow.Infrastructure.Persistence;
using CashFlow.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CashFlow.Api;

public sealed class Bootstrapper
{
    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        ConfigureAuthenticationAndAuthorization(services, configuration);
        ConfigureRateLimiting(services, configuration);
        ConfigureObservabilityAndHealthChecks(services);
        ConfigureSwagger(services);
    }

    private static void ConfigureAuthenticationAndAuthorization(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("CanWriteCashFlow", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole("Merchant")
                      .RequireClaim("scope", "cashflow.write"))
            .AddPolicy("CanReadCashFlow", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole("Merchant")
                      .RequireClaim("scope", "cashflow.read"));
    }

    private static void ConfigureRateLimiting(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var authPermitLimit = configuration.GetValue("RateLimiting:AuthPermitLimit", 20);
        var apiPermitLimit = configuration.GetValue("RateLimiting:ApiPermitLimit", 200);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter("AuthPolicy", limiterOptions =>
            {
                limiterOptions.PermitLimit = authPermitLimit;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            options.AddFixedWindowLimiter("ApiPolicy", limiterOptions =>
            {
                limiterOptions.PermitLimit = apiPermitLimit;
                limiterOptions.Window = TimeSpan.FromSeconds(1);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 10;
            });
        });
    }

    private static void ConfigureObservabilityAndHealthChecks(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("CashFlow.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        services.AddHealthChecks()
            .AddDbContextCheck<CashFlowDbContext>(name: "postgresql", tags: ["ready"]);
    }

    private static void ConfigureSwagger(IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "CashFlow API — Controle de Lançamentos & Consolidado Diário",
                Version = "v1",
                Description = "API para ingestão rápida de lançamentos financeiros (202 Accepted via fila) e consulta pré-computada do saldo diário consolidado (Redis + PostgreSQL)."
            });

            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Informe o token JWT no formato: Bearer {seu_token}",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            };

            options.AddSecurityDefinition("Bearer", securityScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { securityScheme, Array.Empty<string>() }
            });
        });
    }
}
