using System.Security.Claims;

namespace API;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Missing user id.");
        return Guid.Parse(value);
    }
}
