using Microsoft.AspNetCore.Identity;

namespace ClaimBridge.Identity.Data;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";

    // null for clearinghouse staff; provider users are pinned to one organisation
    public Guid? OrgId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}
