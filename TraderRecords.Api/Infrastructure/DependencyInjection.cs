using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TraderRecords.Api.API.ErrorHandling;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Application.Services;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Infrastructure.Authentication;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Api.Infrastructure;

/// <summary>
/// Infrastructure composition root. Keeps persistence, authentication, token and
/// application-service registrations out of Program.cs while retaining explicit DI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTraderRecordsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        // TimeProvider keeps time-dependent behaviour replaceable with a fixed clock in tests.
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        // Use the framework password hasher; no custom password algorithm is introduced.
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITokenService, JwtTokenService>();

        if (environment.IsDevelopment())
        {
            services.AddScoped<DevelopmentSeeder>();
        }

        services.AddJwtAuthentication(configuration);

        return services;
    }

    private static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind and validate JWT settings before any token can be created. A missing
        // or short signing key is a startup configuration error, not a runtime surprise.
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 bytes. " +
                "Use dotnet user-secrets or the Jwt__SigningKey environment variable.");
        }

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer),
                "Jwt:Issuer is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Audience),
                "Jwt:Audience is required.")
            .Validate(
                options => options.ExpiryMinutes > 0,
                "Jwt:ExpiryMinutes must be positive.")
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 bytes.")
            .ValidateOnStart();

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the compact claim names ("sub", "role") instead of the framework's
                // default inbound claim mapping. Ownership checks read "sub" explicitly.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    NameClaimType = JwtClaimNames.Subject,
                    RoleClaimType = JwtClaimNames.Role,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers["WWW-Authenticate"] = "Bearer";

                        var problem = ApiProblemDetails.Create(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "UNAUTHORIZED",
                            "Authentication is required.");

                        await context.Response.WriteAsJsonAsync(
                            problem,
                            options: null,
                            contentType: "application/problem+json",
                            cancellationToken: context.HttpContext.RequestAborted);
                    },
                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;

                        var problem = ApiProblemDetails.Create(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "FORBIDDEN",
                            "You do not have permission to access this resource.");

                        await context.Response.WriteAsJsonAsync(
                            problem,
                            options: null,
                            contentType: "application/problem+json",
                            cancellationToken: context.HttpContext.RequestAborted);
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
