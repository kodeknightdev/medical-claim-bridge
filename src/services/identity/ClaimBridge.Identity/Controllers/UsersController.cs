using ClaimBridge.Contracts.Reference;
using ClaimBridge.Identity.Auth;
using ClaimBridge.Identity.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Identity.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(UserManager<AppUser> users) : ControllerBase
{
    public sealed record UserDto(Guid Id, string Email, string DisplayName, Guid? OrgId, IList<string> Roles, DateTimeOffset? LastLoginAt);

    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.ProviderAdmin)]
    public async Task<ActionResult<IEnumerable<UserDto>>> List()
    {
        var query = users.Users.AsNoTracking();
        if (!User.IsInRole(Roles.Admin))
        {
            var orgId = User.OrgId();
            query = query.Where(u => u.OrgId == orgId);
        }

        var result = new List<UserDto>();
        foreach (var u in await query.OrderBy(u => u.Email).ToListAsync())
            result.Add(new UserDto(u.Id, u.Email!, u.DisplayName, u.OrgId, await users.GetRolesAsync(u), u.LastLoginAt));
        return result;
    }

    [HttpGet("me")]
    public ActionResult<object> Me() => new
    {
        sub = User.FindFirst("sub")?.Value,
        name = User.Identity?.Name,
        orgId = User.OrgId(),
        roles = User.FindAll("role").Select(c => c.Value)
    };
}
