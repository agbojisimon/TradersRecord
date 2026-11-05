using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Consent;

namespace TraderRecords.Api.Application.Interfaces;

public interface IConsentService
{
    Task<Result<ConsentResponse>> GrantAsync(
        Guid traderUserId,
        Guid lenderId,
        CancellationToken ct);

    Task<Result<Unit>> RevokeAsync(
        Guid traderUserId,
        Guid lenderId,
        CancellationToken ct);
}
