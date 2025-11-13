using System.Security.Claims;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimBridge.Security.Tests;

public class AuthorizationTests
{
    private static readonly Guid Sunrise = SeedIds.SunriseOrg;
    private static readonly Guid Lakeside = SeedIds.LakesideOrg;
    private readonly IAuthorizationService _authz;

    public AuthorizationTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddClaimBridgeAuth();
        _authz = builder.Build().Services.GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(string scope, string? role = null, Guid? org = null)
    {
        var claims = new List<Claim> { new("sub", "x"), new("scope", scope) };
        if (role is not null) claims.Add(new Claim("role", role));
        if (org is not null) claims.Add(new Claim(ClaimTypesEx.OrgId, org.ToString()!));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", "role"));
    }

    [Theory]
    [InlineData("claims.submit claims.read", Policies.ClaimsSubmit, true)]
    [InlineData("claims.read", Policies.ClaimsSubmit, false)]
    [InlineData("claims.read", Policies.ClaimsRead, true)]
    [InlineData("api", Policies.ClaimsRead, true)]
    [InlineData("claims.submit", Policies.ClaimsRead, false)]
    public async Task Client_credentials_scopes_gate_the_submission_api(string scope, string policy, bool allowed) =>
        (await _authz.AuthorizeAsync(Principal(scope, org: Sunrise), policy)).Succeeded.ShouldBe(allowed);

    [Theory]
    [InlineData(Roles.Admin, Policies.Staff, true)]
    [InlineData(Roles.OpsAnalyst, Policies.Staff, true)]
    [InlineData(Roles.ProviderAdmin, Policies.Staff, false)]
    [InlineData(Roles.ProviderAdmin, Policies.ProviderAdmin, true)]
    [InlineData(Roles.ProviderBiller, Policies.ProviderAdmin, false)]
    [InlineData(Roles.OpsAnalyst, Policies.Admin, false)]
    public async Task Role_policies(string role, string policy, bool allowed) =>
        (await _authz.AuthorizeAsync(Principal("api", role), policy)).Succeeded.ShouldBe(allowed);

    [Fact]
    public void Provider_users_are_pinned_to_their_own_organisation()
    {
        var biller = Principal("api", Roles.ProviderBiller, Sunrise);
        biller.CanAccessOrg(Sunrise).ShouldBeTrue();
        biller.CanAccessOrg(Lakeside).ShouldBeFalse();
        biller.OrgFilter(Lakeside).ShouldBe(Sunrise);
    }

    [Fact]
    public void Staff_see_every_organisation_and_may_filter()
    {
        var ops = Principal("api", Roles.OpsAnalyst);
        ops.CanAccessOrg(Lakeside).ShouldBeTrue();
        ops.OrgFilter().ShouldBeNull();
        ops.OrgFilter(Lakeside).ShouldBe(Lakeside);
    }

    [Fact]
    public void A_provider_token_without_an_org_sees_nothing()
    {
        var odd = Principal("api", Roles.ProviderBiller);
        odd.OrgFilter().ShouldBe(Guid.Empty);
        odd.CanAccessOrg(Sunrise).ShouldBeFalse();
    }
}
