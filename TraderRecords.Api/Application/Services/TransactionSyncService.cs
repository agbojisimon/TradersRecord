using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Sync;
using TraderRecords.Api.Application.Interfaces;
using TraderRecords.Api.Domain.Entities;
using TraderRecords.Api.Domain.Enums;
using TraderRecords.Api.Infrastructure.Data;

namespace TraderRecords.Api.Application.Services;

/// <summary>
/// Implements retry-safe, partial-success transaction synchronisation. Items are
/// processed sequentially because the scoped DbContext is not thread-safe; each
/// accepted item is saved before the next item is evaluated.
/// </summary>
public sealed class TransactionSyncService : ITransactionSyncService
{
    private const int MinimumItems = 1;
    private const int MaximumItems = 100;
    private const long MinimumAmountMinor = 1;
    private const long MaximumAmountMinor = 100_000_000_000;
    private const int MaximumNoteLength = 280;
    private const int MaximumBusyAttempts = 3;
    private const int SqliteConstraintErrorCode = 19;
    private const int SqliteBusyErrorCode = 5;
    private const int SqliteLockedErrorCode = 6;
    private const string BusyMessage = "The database is busy. Retry the request.";

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public TransactionSyncService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Result<SyncBatchResponse>> SyncAsync(
        Guid traderUserId,
        SyncBatchRequest request,
        CancellationToken ct)
    {
        var businessId = await _db.Businesses
            .AsNoTracking()
            .Where(business => business.OwnerUserId == traderUserId)
            .Select(business => (Guid?)business.Id)
            .SingleOrDefaultAsync(ct);

        if (businessId is null)
        {
            return Result<SyncBatchResponse>.Failure(
                Error.NotFound("BUSINESS_NOT_FOUND", "The trader's business was not found."));
        }

        var envelopeError = ValidateEnvelope(request, out var items);

        if (envelopeError is not null)
        {
            return Result<SyncBatchResponse>.Failure(envelopeError);
        }

        // One instant is used for the entire batch so an item cannot move out of the
        // admission window merely because earlier items took time to persist.
        var nowUtc = TruncateToMilliseconds(_timeProvider.GetUtcNow().UtcDateTime);
        var oldestAllowedUtc = nowUtc.AddDays(-365);

        var results = new List<SyncItemResult>(items.Count);
        var acceptedCount = 0;
        var duplicateCount = 0;
        var rejectedCount = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var itemResult = item is null
                ? Result<SyncItemResult>.Success(
                    Rejected(index, null, "INVALID_ITEM", null, "A null item is not valid."))
                : await ProcessItemAsync(
                    businessId.Value,
                    item,
                    index,
                    nowUtc,
                    oldestAllowedUtc,
                    ct);

            if (!itemResult.IsSuccess)
            {
                // Busy/locked is request-wide. Earlier accepted items are intentionally
                // not rolled back; the client can safely retry the full batch.
                return Result<SyncBatchResponse>.Failure(itemResult.Error);
            }

            var result = itemResult.Value;
            results.Add(result);

            switch (result.Status)
            {
                case SyncItemStatus.Accepted:
                    acceptedCount++;
                    break;
                case SyncItemStatus.Duplicate:
                    duplicateCount++;
                    break;
                case SyncItemStatus.Rejected:
                    rejectedCount++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported sync item status '{result.Status}'.");
            }
        }

        return Result<SyncBatchResponse>.Success(new SyncBatchResponse
        {
            ReceivedCount = items.Count,
            AcceptedCount = acceptedCount,
            DuplicateCount = duplicateCount,
            RejectedCount = rejectedCount,
            Results = results
        });
    }

    private async Task<Result<SyncItemResult>> ProcessItemAsync(
        Guid businessId,
        SyncItemRequest item,
        int index,
        DateTime nowUtc,
        DateTime oldestAllowedUtc,
        CancellationToken ct)
    {
        if (item.ClientId is null || item.ClientId == Guid.Empty)
        {
            return Result<SyncItemResult>.Success(
                Rejected(
                    index,
                    item.ClientId,
                    "CLIENT_ID_REQUIRED",
                    "clientId",
                    "A non-empty clientId is required."));
        }

        var clientId = item.ClientId.Value;

        if (!TryParseType(item.Type, out var type))
        {
            return Result<SyncItemResult>.Success(
                Rejected(
                    index,
                    clientId,
                    "INVALID_TYPE",
                    "type",
                    "Type must be exactly Sale or Expense."));
        }

        if (item.AmountMinor is null
            || item.AmountMinor < MinimumAmountMinor
            || item.AmountMinor > MaximumAmountMinor)
        {
            return Result<SyncItemResult>.Success(
                Rejected(
                    index,
                    clientId,
                    "INVALID_AMOUNT",
                    "amountMinor",
                    $"AmountMinor must be between {MinimumAmountMinor} and {MaximumAmountMinor}."));
        }

        var note = NormalizeNote(item.Note);

        if (note is { Length: > MaximumNoteLength })
        {
            return Result<SyncItemResult>.Success(
                Rejected(
                    index,
                    clientId,
                    "NOTE_TOO_LONG",
                    "note",
                    $"Note must be {MaximumNoteLength} characters or fewer after trimming."));
        }

        if (item.OccurredAt is null)
        {
            return Result<SyncItemResult>.Success(
                Rejected(
                    index,
                    clientId,
                    "INVALID_OCCURRED_AT",
                    "occurredAt",
                    "An occurrence timestamp with an explicit UTC offset is required."));
        }

        var amountMinor = item.AmountMinor.Value;
        var occurredAtUtc = TruncateToMilliseconds(item.OccurredAt.Value.UtcDateTime);

        // Existing IDs are checked before the age window so a historical entry that
        // was accepted earlier remains a Duplicate when retried after 365 days.
        var existingResult = await FindExistingAsync(businessId, clientId, ct);

        if (!existingResult.IsSuccess)
        {
            return Result<SyncItemResult>.Failure(existingResult.Error);
        }

        if (existingResult.Value is { } existing)
        {
            return Result<SyncItemResult>.Success(
                ClassifyExisting(
                    index,
                    clientId,
                    existing,
                    type,
                    amountMinor,
                    note,
                    occurredAtUtc));
        }

        if (occurredAtUtc < oldestAllowedUtc || occurredAtUtc > nowUtc)
        {
            return Result<SyncItemResult>.Success(
                Rejected(
                    index,
                    clientId,
                    "INVALID_OCCURRED_AT",
                    "occurredAt",
                    "A new occurrence must be within the last 365 days and cannot be in the future."));
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            ClientId = clientId,
            Type = type,
            AmountMinor = amountMinor,
            Note = note,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = nowUtc
        };

        return await SaveNewTransactionAsync(
            transaction,
            index,
            clientId,
            type,
            amountMinor,
            note,
            occurredAtUtc,
            ct);
    }

    private async Task<Result<SyncItemResult>> SaveNewTransactionAsync(
        Transaction transaction,
        int index,
        Guid clientId,
        TransactionType type,
        long amountMinor,
        string? note,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        _db.Transactions.Add(transaction);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(ct);

                return Result<SyncItemResult>.Success(
                    new SyncItemResult
                    {
                        Index = index,
                        ClientId = clientId,
                        Status = SyncItemStatus.Accepted,
                        TransactionId = transaction.Id
                    });
            }
            catch (DbUpdateException exception) when (IsClientIdUniquenessViolation(exception))
            {
                // A concurrent request won the unique-index race after our pre-check.
                // Detach only the failed insert, then read the stored winner and apply
                // the same canonical comparison used by the normal duplicate path.
                _db.Entry(transaction).State = EntityState.Detached;

                var existingResult = await FindExistingAsync(
                    transaction.BusinessId,
                    transaction.ClientId,
                    ct);

                if (!existingResult.IsSuccess)
                {
                    return Result<SyncItemResult>.Failure(existingResult.Error);
                }

                if (existingResult.Value is not { } existing)
                {
                    throw new InvalidOperationException(
                        "A client-ID uniqueness violation occurred, but the stored transaction could not be found.",
                        exception);
                }

                return Result<SyncItemResult>.Success(
                    ClassifyExisting(
                        index,
                        clientId,
                        existing,
                        type,
                        amountMinor,
                        note,
                        occurredAtUtc));
            }
            catch (Exception exception) when (IsBusy(exception) && attempt < MaximumBusyAttempts)
            {
                await Task.Delay(BusyRetryDelay(attempt), ct);
            }
            catch (Exception exception) when (IsBusy(exception))
            {
                _db.Entry(transaction).State = EntityState.Detached;

                return Result<SyncItemResult>.Failure(
                    Error.Unavailable("DATABASE_BUSY", BusyMessage));
            }
        }
    }

    private async Task<Result<Transaction?>> FindExistingAsync(
        Guid businessId,
        Guid clientId,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var existing = await _db.Transactions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        transaction => transaction.BusinessId == businessId
                            && transaction.ClientId == clientId,
                        ct);

                return Result<Transaction?>.Success(existing);
            }
            catch (SqliteException exception) when (IsBusy(exception) && attempt < MaximumBusyAttempts)
            {
                await Task.Delay(BusyRetryDelay(attempt), ct);
            }
            catch (SqliteException exception) when (IsBusy(exception))
            {
                return Result<Transaction?>.Failure(
                    Error.Unavailable("DATABASE_BUSY", BusyMessage));
            }
        }
    }

    private static Error? ValidateEnvelope(
        SyncBatchRequest? request,
        out IReadOnlyList<SyncItemRequest?> items)
    {
        items = Array.Empty<SyncItemRequest?>();

        if (request?.Items is null)
        {
            return EnvelopeValidationError("An items array is required.");
        }

        if (request.Items.Count is < MinimumItems or > MaximumItems)
        {
            return EnvelopeValidationError(
                $"Items must contain between {MinimumItems} and {MaximumItems} entries.");
        }

        items = request.Items;
        return null;
    }

    private static Error EnvelopeValidationError(string message)
    {
        return Error.Validation(
            "One or more validation errors occurred.",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["items"] = [message]
            });
    }

    private static SyncItemResult ClassifyExisting(
        int index,
        Guid clientId,
        Transaction existing,
        TransactionType type,
        long amountMinor,
        string? note,
        DateTime occurredAtUtc)
    {
        var canonicalMatch = existing.Type == type
            && existing.AmountMinor == amountMinor
            && string.Equals(NormalizeNote(existing.Note), note, StringComparison.Ordinal)
            && TruncateToMilliseconds(existing.OccurredAtUtc) == occurredAtUtc;

        if (canonicalMatch)
        {
            return new SyncItemResult
            {
                Index = index,
                ClientId = clientId,
                Status = SyncItemStatus.Duplicate,
                TransactionId = existing.Id
            };
        }

        return Rejected(
            index,
            clientId,
            "CLIENT_ID_CONFLICT",
            "clientId",
            "This clientId already belongs to a transaction with different canonical fields.");
    }

    private static SyncItemResult Rejected(
        int index,
        Guid? clientId,
        string code,
        string? field,
        string message)
    {
        return new SyncItemResult
        {
            Index = index,
            ClientId = clientId,
            Status = SyncItemStatus.Rejected,
            Errors =
            [
                new ItemError
                {
                    Code = code,
                    Field = field,
                    Message = message
                }
            ]
        };
    }

    private static bool TryParseType(string? value, out TransactionType type)
    {
        if (string.Equals(value, nameof(TransactionType.Sale), StringComparison.Ordinal))
        {
            type = TransactionType.Sale;
            return true;
        }

        if (string.Equals(value, nameof(TransactionType.Expense), StringComparison.Ordinal))
        {
            type = TransactionType.Expense;
            return true;
        }

        type = default;
        return false;
    }

    private static string? NormalizeNote(string? note)
    {
        if (note is null)
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

        return new DateTime(
            utcValue.Ticks - (utcValue.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }

    private static bool IsClientIdUniquenessViolation(DbUpdateException exception)
    {
        // Only the scoped client-ID index is treated as an idempotency race. Any other
        // constraint failure must remain an unexpected server error.
        return exception.InnerException is SqliteException
            {
                SqliteErrorCode: SqliteConstraintErrorCode
            } sqliteException
            && (sqliteException.Message.Contains(
                    "Transactions.BusinessId, Transactions.ClientId",
                    StringComparison.OrdinalIgnoreCase)
                || sqliteException.Message.Contains(
                    "IX_Transactions_BusinessId_ClientId",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBusy(Exception exception)
    {
        return exception is SqliteException sqliteException
                && IsBusyErrorCode(sqliteException.SqliteErrorCode)
            || exception is DbUpdateException
                {
                    InnerException: SqliteException innerSqliteException
                }
                && IsBusyErrorCode(innerSqliteException.SqliteErrorCode);
    }

    private static bool IsBusyErrorCode(int errorCode)
    {
        return errorCode is SqliteBusyErrorCode or SqliteLockedErrorCode;
    }

    private static TimeSpan BusyRetryDelay(int attempt)
    {
        return TimeSpan.FromMilliseconds(25 * attempt);
    }
}
