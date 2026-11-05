using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Consent;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Api.Application.Services;

/// <summary>
/// Maintains one current-state consent row per business/lender pair. The trader's
/// business is always resolved from the validated subject claim, never from a
/// caller-supplied business ID.
/// </summary>
public sealed class ConsentService : IConsentService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ConsentService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ConsentResponse>> GrantAsync(
        Guid traderUserId,
        Guid lenderId,
        CancellationToken ct)
    {
        if (lenderId == Guid.Empty)
        {
            return Result<ConsentResponse>.Failure(InvalidLenderId());
        }

        var business = await _db.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OwnerUserId == traderUserId,
                ct);

        if (business is null)
        {
            return Result<ConsentResponse>.Failure(
                Error.NotFound(
                    "BUSINESS_NOT_FOUND",
                    "The trader's business was not found."));
        }

        var lenderResult = await FindLenderAsync(lenderId, ct);

        if (!lenderResult.IsSuccess)
        {
            return Result<ConsentResponse>.Failure(lenderResult.Error);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var consent = await _db.Consents.SingleOrDefaultAsync(
            candidate => candidate.BusinessId == business.Id
                && candidate.LenderUserId == lenderId,
            ct);

        if (consent is null)
        {
            consent = new Consent
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                LenderUserId = lenderId,
                GrantedAtUtc = nowUtc
            };

            _db.Consents.Add(consent);
        }
        else if (consent.RevokedAtUtc is not null)
        {
            // Regrant after revocation starts a new active period but keeps the
            // single current-state row. A grant while already active is a no-op.
            consent.GrantedAtUtc = nowUtc;
            consent.RevokedAtUtc = null;
        }
        else
        {
            return Result<ConsentResponse>.Success(ToResponse(consent));
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsConsentUniquenessViolation(exception))
        {
            // Two first-time grants can race after both miss the lookup. The unique
            // index is the final guard; return the row the winner persisted.
            _db.ChangeTracker.Clear();

            var persisted = await _db.Consents
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.BusinessId == business.Id
                        && candidate.LenderUserId == lenderId,
                    ct);

            return Result<ConsentResponse>.Success(ToResponse(persisted));
        }

        return Result<ConsentResponse>.Success(ToResponse(consent));
    }

    public async Task<Result<Unit>> RevokeAsync(
        Guid traderUserId,
        Guid lenderId,
        CancellationToken ct)
    {
        if (lenderId == Guid.Empty)
        {
            return Result<Unit>.Failure(InvalidLenderId());
        }

        var business = await _db.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OwnerUserId == traderUserId,
                ct);

        if (business is null)
        {
            return Result<Unit>.Failure(
                Error.NotFound(
                    "BUSINESS_NOT_FOUND",
                    "The trader's business was not found."));
        }

        var lenderResult = await FindLenderAsync(lenderId, ct);

        if (!lenderResult.IsSuccess)
        {
            return Result<Unit>.Failure(lenderResult.Error);
        }

        var consent = await _db.Consents.SingleOrDefaultAsync(
            candidate => candidate.BusinessId == business.Id
                && candidate.LenderUserId == lenderId,
            ct);

        // Revoking an absent or already-revoked consent succeeds without creating
        // a row or changing the original revocation timestamp.
        if (consent is null || consent.RevokedAtUtc is not null)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        consent.RevokedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        return Result<Unit>.Success(Unit.Value);
    }

    private async Task<Result<User>> FindLenderAsync(Guid lenderId, CancellationToken ct)
    {
        var lender = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == lenderId, ct);

        if (lender is null)
        {
            return Result<User>.Failure(
                Error.NotFound("LENDER_NOT_FOUND", "The lender was not found."));
        }

        if (lender.Role != UserRole.Lender)
        {
            return Result<User>.Failure(
                Error.Conflict("TARGET_NOT_LENDER", "The target user is not a Lender."));
        }

        return Result<User>.Success(lender);
    }

    private static ConsentResponse ToResponse(Consent consent)
    {
        return new ConsentResponse
        {
            BusinessId = consent.BusinessId,
            LenderId = consent.LenderUserId,
            IsActive = consent.RevokedAtUtc is null,
            GrantedAtUtc = consent.GrantedAtUtc,
            RevokedAtUtc = consent.RevokedAtUtc
        };
    }

    private static Error InvalidLenderId()
    {
        return Error.Validation(
            "A valid lender ID is required.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["lenderId"] = ["A non-empty lender ID is required."]
            });
    }

    private static bool IsConsentUniquenessViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException { SqliteErrorCode: 19 } sqliteException
            && (sqliteException.Message.Contains(
                    "Consents.BusinessId, Consents.LenderUserId",
                    StringComparison.OrdinalIgnoreCase)
                || sqliteException.Message.Contains(
                    "IX_Consents_BusinessId_LenderUserId",
                    StringComparison.OrdinalIgnoreCase));
    }
}
