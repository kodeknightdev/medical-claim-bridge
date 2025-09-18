namespace ClaimBridge.Claims.Application.Clients;

public sealed record RiskFactor(string Factor, string Label, double Impact);

public sealed record RiskScore(double Probability, string RiskBand, string ModelVersion, IReadOnlyList<RiskFactor> Factors, bool Demo = true);

public class RiskClient(HttpClient http, ILogger<RiskClient> logger)
{
    public async Task<RiskScore?> ScoreAsync(Guid? claimId, Guid orgId, ClaimRequest claim, DateOnly submissionDate, CancellationToken ct)
    {
        var lines = claim.Lines ?? [];
        var features = new
        {
            claimId,
            orgId,
            payerId = claim.PayerId,
            procedureCodes = lines.Select(l => l.ProcedureCode).ToArray(),
            modifiers = lines.SelectMany(l => l.Modifiers ?? []).Distinct().ToArray(),
            lineCharges = lines.Select(l => l.Charge).ToArray(),
            units = lines.Select(l => l.Units).ToArray(),
            placeOfService = lines.FirstOrDefault()?.PlaceOfService ?? "11",
            diagnosisCount = claim.Diagnoses?.Count ?? 0,
            diagnoses = claim.Diagnoses ?? [],
            hasPriorAuth = !string.IsNullOrWhiteSpace(claim.PriorAuthNumber),
            frequencyCode = claim.FrequencyCode,
            daysToSubmit = lines.Count == 0 ? 0 : submissionDate.DayNumber - lines.Min(l => l.ServiceDate).DayNumber,
            patientAge = claim.Patient is null ? 40 : submissionDate.Year - claim.Patient.DateOfBirth.Year
        };

        try
        {
            using var response = await http.PostAsJsonAsync("internal/score", features, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<RiskScore>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // the score is optional; never hold a claim up because the model service is slow
            logger.LogInformation("Denial risk unavailable: {Error}", ex.Message);
            return null;
        }
    }
}
