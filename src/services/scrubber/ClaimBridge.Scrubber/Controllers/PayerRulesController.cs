using System.ComponentModel.DataAnnotations;
using ClaimBridge.Scrubber.Data;
using ClaimBridge.Scrubber.Rules;
using ClaimBridge.Scrubber.Rules.PayerRules;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Scrubber.Controllers;

[ApiController]
[Route("api/payer-rules")]
[Authorize(Policy = Policies.Staff)]
public class PayerRulesController(ScrubberDb db, PayerRuleProvider provider, ScrubEngine engine, TimeProvider clock) : ControllerBase
{
    public sealed record UpsertPayerRule(
        [property: Required, StringLength(20)] string PayerId,
        [property: Required, StringLength(20)] string RuleCode,
        [property: Required, StringLength(120)] string Name,
        [property: RegularExpression("line|claim")] string Scope,
        [property: Required] string ConditionJson,
        [property: RegularExpression("Error|Warning")] string Severity,
        [property: Required, StringLength(400)] string Message,
        [property: StringLength(400)] string? FixSuggestion,
        bool Enabled = true);

    public sealed record ToggleRule(bool Enabled, [property: RegularExpression("Error|Warning")] string? Severity);

    [HttpGet]
    public async Task<IEnumerable<PayerRule>> List([FromQuery] string? payerId) =>
        await db.PayerRules.AsNoTracking()
            .Where(r => payerId == null || r.PayerId == payerId)
            .OrderBy(r => r.PayerId).ThenBy(r => r.RuleCode)
            .ToListAsync();

    [HttpGet("built-in")]
    public IEnumerable<object> BuiltIn() => engine.BuiltInRules.Select(r => new { r.Code, r.Name });

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Create(UpsertPayerRule request)
    {
        try { PayerRuleCompiler.Compile(request.ConditionJson); }
        catch (PayerRuleSyntaxException ex) { return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest); }

        if (await db.PayerRules.AnyAsync(r => r.PayerId == request.PayerId && r.RuleCode == request.RuleCode))
            return Problem("Rule code already exists for this payer.", statusCode: StatusCodes.Status409Conflict);

        var rule = new PayerRule
        {
            PayerId = request.PayerId, RuleCode = request.RuleCode, Name = request.Name, Scope = request.Scope,
            ConditionJson = request.ConditionJson, Severity = request.Severity, Message = request.Message,
            FixSuggestion = request.FixSuggestion, Enabled = request.Enabled, UpdatedAt = clock.GetUtcNow(), UpdatedBy = User.Actor()
        };
        db.PayerRules.Add(rule);
        await db.SaveChangesAsync();
        await provider.InvalidateAsync(rule.PayerId);
        return Created($"/api/payer-rules/{rule.Id}", rule);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Toggle(int id, ToggleRule request)
    {
        var rule = await db.PayerRules.FindAsync(id);
        if (rule is null) return NotFound();
        rule.Enabled = request.Enabled;
        if (request.Severity is not null) rule.Severity = request.Severity;
        rule.UpdatedAt = clock.GetUtcNow();
        rule.UpdatedBy = User.Actor();
        await db.SaveChangesAsync();
        await provider.InvalidateAsync(rule.PayerId);
        return NoContent();
    }
}
