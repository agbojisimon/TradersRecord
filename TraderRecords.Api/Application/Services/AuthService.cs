using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Api.Application.Services;

/// <summary>
/// Owns password verification and the atomic Trader-plus-business registration
/// workflow. Expected failures are returned as Result values; unexpected database
/// faults bubble to the central exception handler.
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateRegistration(
            request,
            out var fullName,
            out var email,
            out var normalizedEmail,
            out var businessName);

        if (validationError is not null)
        {
            return Result<AuthResponse>.Failure(validationError);
        }

        // This check gives a normal duplicate response. The unique index remains the
        // final guard for two registrations racing between this check and the insert.
        var emailAlreadyExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(user => user.NormalizedEmail == normalizedEmail, ct);

        if (emailAlreadyExists)
        {
            return Result<AuthResponse>.Failure(
                Error.Conflict("EMAIL_IN_USE", "Email is already in use."));
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = UserRole.Trader,
            CreatedAtUtc = nowUtc
        };

        // Passwords are never trimmed: leading or trailing whitespace is part of the
        // secret and must be verified exactly as supplied.
        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.Password ?? string.Empty);

        var business = new Business
        {
            Id = Guid.NewGuid(),
            OwnerUserId = user.Id,
            Name = businessName,
            Currency = "NGN",
            CreatedAtUtc = nowUtc
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.Users.Add(user);
        _db.Businesses.Add(business);

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException exception) when (IsEmailUniquenessViolation(exception))
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            return Result<AuthResponse>.Failure(
                Error.Conflict("EMAIL_IN_USE", "Email is already in use."));
        }

        return Result<AuthResponse>.Success(_tokenService.Create(user, business.Id));
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (email.Length is < 3 or > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length > 128)
        {
            errors["password"] = ["Password must be 128 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return Result<AuthResponse>.Failure(
                Error.Validation("One or more validation errors occurred.", errors));
        }

        var normalizedEmail = email.ToUpperInvariant();
        var user = await _db.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            ct);

        if (user is null)
        {
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("INVALID_CREDENTIALS", InvalidCredentialsMessage));
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("INVALID_CREDENTIALS", InvalidCredentialsMessage));
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            await _db.SaveChangesAsync(ct);
        }

        var businessId = await _db.Businesses
            .AsNoTracking()
            .Where(business => business.OwnerUserId == user.Id)
            .Select(business => (Guid?)business.Id)
            .SingleOrDefaultAsync(ct);

        return Result<AuthResponse>.Success(_tokenService.Create(user, businessId));
    }

    private static Error? ValidateRegistration(
        RegisterRequest request,
        out string fullName,
        out string email,
        out string normalizedEmail,
        out string businessName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        fullName = (request.FullName ?? string.Empty).Trim();
        if (fullName.Length is < 2 or > 100)
        {
            errors["fullName"] = ["Full name must be between 2 and 100 characters."];
        }

        email = (request.Email ?? string.Empty).Trim();
        normalizedEmail = email.ToUpperInvariant();
        if (email.Length is < 3 or > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["A valid email address is required."];
        }

        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length is < 12 or > 128)
        {
            errors["password"] = ["Password must be between 12 and 128 characters."];
        }

        businessName = (request.BusinessName ?? string.Empty).Trim();
        if (businessName.Length is < 2 or > 120)
        {
            errors["businessName"] = ["Business name must be between 2 and 120 characters."];
        }

        return errors.Count == 0
            ? null
            : Error.Validation("One or more validation errors occurred.", errors);
    }

    private static bool IsEmailUniquenessViolation(DbUpdateException exception)
    {
        // SQLite reports constraint failures with code 19. Only the email index is
        // treated as a registration race; every other database fault remains a 500.
        return exception.InnerException is SqliteException { SqliteErrorCode: 19 } sqliteException
            && (sqliteException.Message.Contains(
                    "Users.NormalizedEmail",
                    StringComparison.OrdinalIgnoreCase)
                || sqliteException.Message.Contains(
                    "IX_Users_NormalizedEmail",
                    StringComparison.OrdinalIgnoreCase));
    }
}
