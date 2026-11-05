using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Summary;

namespace TraderRecords.Api.Application.Interfaces;

public interface ISummaryService
{
    Task<Result<BusinessSummaryResponse>> GetForTraderAsync(
        Guid traderUserId,
        CancellationToken ct);

    Task<Result<BusinessSummaryResponse>> GetForLenderAsync(
        Guid lenderUserId,
        Guid businessId,
        CancellationToken ct);
}
