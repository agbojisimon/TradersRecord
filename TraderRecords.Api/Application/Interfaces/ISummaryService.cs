using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Statistics;
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

    Task<Result<PeriodStatisticsResponse>> GetStatisticsForTraderAsync(
        Guid traderUserId,
        DateOnly? fromLocalDate,
        DateOnly? toLocalDate,
        string? groupBy,
        CancellationToken ct);

    Task<Result<PeriodStatisticsResponse>> GetStatisticsForLenderAsync(
        Guid lenderUserId,
        Guid businessId,
        DateOnly? fromLocalDate,
        DateOnly? toLocalDate,
        string? groupBy,
        CancellationToken ct);
}
