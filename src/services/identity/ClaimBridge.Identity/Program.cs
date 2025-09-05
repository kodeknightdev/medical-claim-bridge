using ClaimBridge.Identity.Auth;
using ClaimBridge.Identity.Data;
using ClaimBridge.Identity.Seeding;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using CbScopes = ClaimBridge.Contracts.Reference.Scopes;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("identity");

builder.Services.AddDbContext<IdentityDb>(o =>
{
    o.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDb"));
    o.UseOpenIddict();
});

builder.Services
    .AddIdentity<AppUser, AppRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.ClaimsIdentity.UserNameClaimType = Claims.Name;
        o.ClaimsIdentity.UserIdClaimType = Claims.Subject;
        o.ClaimsIdentity.RoleClaimType = Claims.Role;
    })
    .AddEntityFrameworkStores<IdentityDb>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "cb.identity";
    o.LoginPath = "/account/login";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
});

var issuer = builder.Configuration["Auth:Issuer"];
builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<IdentityDb>())
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetUserInfoEndpointUris("connect/userinfo")
            .SetEndSessionEndpointUris("connect/logout");

        o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
        o.AllowRefreshTokenFlow();

        o.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess,
            CbScopes.Api, CbScopes.ClaimsRead, CbScopes.ClaimsSubmit);

        o.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        o.SetRefreshTokenLifetime(TimeSpan.FromHours(8));
        // refresh tokens roll on every use; a replayed old one past the leeway revokes the chain
        o.SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrWhiteSpace(issuer))
            o.SetIssuer(new Uri(issuer));

        // TODO: load keys from Key Vault outside of dev, ephemeral keys reset on every restart
        o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        // resource services validate plain signed JWTs against our JWKS
        o.DisableAccessTokenEncryption();

        o.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .DisableTransportSecurityRequirement();
    })
    .AddValidation(o =>
    {
        o.UseLocalServer();
        o.UseAspNetCore();
    });

builder.Services.AddScoped<TokenPrincipalFactory>();
builder.Services.AddAntiforgery(o => o.Cookie.Name = "cb.af");
builder.Services.AddAuthorization(o =>
{
    o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "IdentityDb");
builder.Services.AddControllers();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.All;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<IdentityDb>(async (sp, _) =>
    {
        await IdentitySeeder.SeedAsync(sp, app.Configuration, app.Logger);
        await OpenIddictSeeder.SeedAsync(sp, app.Configuration);
    });

app.Run();

public partial class Program;
