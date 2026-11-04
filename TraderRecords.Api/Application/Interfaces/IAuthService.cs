using TraderRecords.Api.Application.Common;
using TraderRecords.Api.Application.DTOs.Auth;

namespace TraderRecords.Api.Application.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken ct);

    Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken ct);
}
