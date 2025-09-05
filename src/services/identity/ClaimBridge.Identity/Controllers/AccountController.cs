using System.Net;
using ClaimBridge.Identity.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ClaimBridge.Identity.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("account")]
public class AccountController(
    SignInManager<AppUser> signIn,
    UserManager<AppUser> users,
    IAntiforgery antiforgery,
    TimeProvider clock) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl) => LoginPage(returnUrl, error: null);

    [HttpPost("login")]
    public async Task<IActionResult> LoginPost([FromForm] string email, [FromForm] string password, [FromForm] string? returnUrl)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest();

        var result = await signIn.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
            return LoginPage(returnUrl, result.IsLockedOut ? "Account locked, try again later." : "Invalid email or password.");

        var user = await users.FindByEmailAsync(email);
        if (user is not null)
        {
            user.LastLoginAt = clock.GetUtcNow();
            await users.UpdateAsync(user);
        }

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : Redirect("/");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return Redirect("/account/login");
    }

    private ContentResult LoginPage(string? returnUrl, string? error)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var html = LoginTemplate
            .Replace("{{returnUrl}}", WebUtility.HtmlEncode(returnUrl ?? "/"))
            .Replace("{{tokenName}}", tokens.FormFieldName)
            .Replace("{{token}}", tokens.RequestToken)
            .Replace("{{error}}", error is null ? "" : $"<p class=\"error\">{WebUtility.HtmlEncode(error)}</p>");
        return Content(html, "text/html");
    }

    private const string LoginTemplate = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Sign in - claim-bridge</title>
          <style>
            body { font-family: system-ui, sans-serif; background: #f3f5f8; display: grid; place-items: center; min-height: 100vh; margin: 0; }
            form { background: #fff; padding: 2rem; border-radius: 8px; box-shadow: 0 2px 12px rgba(0,0,0,.08); width: 320px; }
            h1 { font-size: 1.25rem; margin: 0 0 .25rem; color: #1d3557; }
            p.sub { color: #667; margin: 0 0 1.25rem; font-size: .85rem; }
            label { display: block; font-size: .85rem; margin: .75rem 0 .25rem; color: #333; }
            input { width: 100%; box-sizing: border-box; padding: .55rem .6rem; border: 1px solid #ccd; border-radius: 4px; }
            button { margin-top: 1.25rem; width: 100%; padding: .6rem; border: 0; border-radius: 4px; background: #1d6fb8; color: #fff; font-weight: 600; cursor: pointer; }
            .error { color: #b3261e; font-size: .85rem; }
          </style>
        </head>
        <body>
          <form method="post" action="/account/login">
            <h1>claim-bridge</h1>
            <p class="sub">Demo environment, synthetic data only.</p>
            {{error}}
            <input type="hidden" name="returnUrl" value="{{returnUrl}}">
            <input type="hidden" name="{{tokenName}}" value="{{token}}">
            <label for="email">Email</label>
            <input id="email" name="email" type="email" autocomplete="username" required autofocus>
            <label for="password">Password</label>
            <input id="password" name="password" type="password" autocomplete="current-password" required>
            <button type="submit">Sign in</button>
          </form>
        </body>
        </html>
        """;
}
