using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Sync;

namespace TraderRecords.Api.Application.Interfaces;

public interface ITransactionSyncService
{
    Task<Result<SyncBatchResponse>> SyncAsync(
        Guid traderUserId,
        SyncBatchRequest request,
        CancellationToken ct);
}
