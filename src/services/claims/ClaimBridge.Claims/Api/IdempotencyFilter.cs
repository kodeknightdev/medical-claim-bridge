using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaimBridge.Claims.Data;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Api;

/// <summary>
/// Idempotency-Key handling for the submission API: the first response for a (client, key) pair is stored and
/// replayed for retries with the same body. A different body under the same key is rejected.
/// </summary>
public sealed class IdempotencyFilter(ClaimsDb db, TimeProvider clock, ILogger<IdempotencyFilter> logger) : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        {
            context.Result = Problem(400, "Idempotency-Key required",
                $"Send a unique {HeaderName} header (max 100 characters) with every submission.");
            return;
        }

        var clientId = http.User.ClientId() ?? http.User.Actor();
        var body = context.ActionArguments.Values.FirstOrDefault(v => v is not null and not CancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, Json))));
        var ct = http.RequestAborted;

        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ClientId == clientId && r.Key == key, ct);
        if (existing is not null)
        {
            context.Result = Replay(existing, hash, http);
            return;
        }

        var record = new IdempotencyRecord { ClientId = clientId, Key = key, RequestHash = hash, CreatedAt = clock.GetUtcNow() };
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // lost the race against a concurrent request with the same key
            db.Entry(record).State = EntityState.Detached;
            context.Result = Problem(409, "Request in progress", "A request with this Idempotency-Key is still being processed.");
            return;
        }

        var executed = await next();

        var (status, value) = executed.Result switch
        {
            ObjectResult o => (o.StatusCode ?? 200, o.Value),
            StatusCodeResult s => (s.StatusCode, null),
            _ => (executed.Exception is null ? 200 : 500, null)
        };

        if (status >= 500 || executed.Exception is not null)
        {
            // let the client retry a server failure with the same key
            db.IdempotencyRecords.Remove(record);
        }
        else
        {
            record.StatusCode = status;
            record.ResponseBody = value is null ? null : JsonSerializer.Serialize(value, value.GetType(), Json);
            record.CompletedAt = clock.GetUtcNow();
        }

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not store idempotent response for key {Key}: {Error}", key, ex.Message);
        }
    }

    private static IActionResult Replay(IdempotencyRecord existing, string hash, HttpContext http)
    {
        if (existing.RequestHash != hash)
            return Problem(422, "Idempotency-Key reused", "This Idempotency-Key was already used with a different request body.");
        if (existing.StatusCode is null)
            return Problem(409, "Request in progress", "A request with this Idempotency-Key is still being processed.");

        http.Response.Headers[ReplayedHeader] = "true";
        return new ContentResult
        {
            StatusCode = existing.StatusCode,
            Content = existing.ResponseBody ?? "",
            ContentType = existing.StatusCode >= 400 ? "application/problem+json" : "application/json"
        };
    }

    private static ObjectResult Problem(int status, string title, string detail) =>
        new(new ProblemDetails { Status = status, Title = title, Detail = detail }) { StatusCode = status };
}
