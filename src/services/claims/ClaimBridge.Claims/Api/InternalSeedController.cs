using ClaimBridge.Claims.Seeding;
using ClaimBridge.ServiceDefaults.Storage;
using Microsoft.AspNetCore.Mvc;

namespace ClaimBridge.Claims.Api;

/// <summary>Development-only import of the synthetic history produced by tools/seed.</summary>
[ApiController]
[Route("internal/seed")]
[ApiExplorerSettings(GroupName = "internal")]
public class InternalSeedController(HistoryImporter importer, IBlobStore blobs, IHostEnvironment env, IConfiguration config) : ControllerBase
{
    public sealed record SeedRequest(string Container, string Name);

    [HttpPost("history")]
    public async Task<IActionResult> History(SeedRequest request, CancellationToken ct)
    {
        if (!env.IsDevelopment() && !config.GetValue<bool>("Seed:Enabled")) return NotFound();
        var records = await HistoryReader.ReadAsync(blobs, new BlobRef(request.Container, request.Name), ct);
        return Ok(new { imported = await importer.ImportAsync(records, ct), total = records.Count });
    }
}
