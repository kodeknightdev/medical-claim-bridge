using ClaimBridge.Remittance.Seeding;
using ClaimBridge.ServiceDefaults.Storage;
using Microsoft.AspNetCore.Mvc;

namespace ClaimBridge.Remittance.Api;

[ApiController]
[Route("internal/seed")]
[ApiExplorerSettings(GroupName = "internal")]
public class InternalSeedController(HistoryImporter importer, IHostEnvironment env, IConfiguration config) : ControllerBase
{
    public sealed record SeedRequest(string Container, string Name);

    [HttpPost("history")]
    public async Task<IActionResult> History(SeedRequest request, CancellationToken ct)
    {
        if (!env.IsDevelopment() && !config.GetValue<bool>("Seed:Enabled")) return NotFound();
        return Ok(new { imported = await importer.ImportAsync(new BlobRef(request.Container, request.Name), ct) });
    }
}
