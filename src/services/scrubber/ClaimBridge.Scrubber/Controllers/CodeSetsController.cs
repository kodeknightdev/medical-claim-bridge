using ClaimBridge.Contracts.ReferenceData;
using ClaimBridge.Scrubber.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Scrubber.Controllers;

[ApiController]
[Route("api/codesets")]
[Authorize]
public class CodeSetsController(ScrubberDb db) : ControllerBase
{
    [HttpGet("icd10")]
    public async Task<IEnumerable<IcdCode>> Icd10([FromQuery] string? search, [FromQuery] int take = 25)
    {
        var query = db.IcdCodes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Code.StartsWith(search) || c.Description.Contains(search));
        return await query.OrderBy(c => c.Code).Take(Math.Clamp(take, 1, 100)).ToListAsync();
    }

    [HttpGet("procedures")]
    public async Task<IEnumerable<ProcedureCode>> Procedures() =>
        await db.ProcedureCodes.AsNoTracking().OrderBy(p => p.Code).ToListAsync();

    [HttpGet("modifiers")]
    public async Task<IEnumerable<Modifier>> Modifiers() =>
        await db.Modifiers.AsNoTracking().OrderBy(m => m.Code).ToListAsync();

    [HttpGet("adjustment-reasons")]
    public object AdjustmentReasonCodes() => new
    {
        groupCodes = AdjustmentReasons.GroupCodes,
        carc = AdjustmentReasons.CarcCodes,
        rarc = AdjustmentReasons.RarcCodes
    };
}
