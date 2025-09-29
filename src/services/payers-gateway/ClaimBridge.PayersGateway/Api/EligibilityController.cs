using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimBridge.PayersGateway.Api;

/// <summary>Real-time 270/271-style eligibility, cached briefly per member, payer and date of service.</summary>
[ApiController]
[Authorize(Policy = Policies.ClaimsRead)]
public class EligibilityController(EligibilityService eligibility) : ControllerBase
{
    [HttpPost("api/eligibility")]
    public Task<EligibilityAnswer> Check(EligibilityRequest request, CancellationToken ct) => eligibility.CheckAsync(request, ct);

    [HttpPost("internal/eligibility")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "internal")]
    public Task<EligibilityAnswer> CheckInternal(EligibilityRequest request, CancellationToken ct) => eligibility.CheckAsync(request, ct);
}
