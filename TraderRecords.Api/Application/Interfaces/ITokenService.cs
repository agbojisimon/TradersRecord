using TraderRecords.Api.Application.DTOs.Auth;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Application.Interfaces;

public interface ITokenService
{
    AuthResponse Create(User user, Guid? businessId);
}
