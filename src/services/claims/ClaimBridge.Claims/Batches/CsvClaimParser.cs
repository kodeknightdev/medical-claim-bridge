using System.Globalization;
using ClaimBridge.Claims.Application;
using CsvHelper;
using CsvHelper.Configuration;

namespace ClaimBridge.Claims.Batches;

public sealed record ParsedClaim(string Reference, ClaimRequest? Request, string? Error);

/// <summary>
/// One row per service line; rows sharing a claim_ref form one claim. Pipe-separated lists for diagnoses,
/// modifiers and pointers keep the file editable in a spreadsheet.
/// </summary>
public static class CsvClaimParser
{
    public const string Header =
        "claim_ref,payer_id,billing_npi,rendering_npi,member_id,patient_first,patient_last,patient_dob,patient_gender," +
        "prior_auth,diagnoses,procedure,modifiers,units,charge,service_date,pos,dx_pointers";

    private sealed class Row
    {
        public string claim_ref { get; set; } = "";
        public string payer_id { get; set; } = "";
        public string billing_npi { get; set; } = "";
        public string? rendering_npi { get; set; }
        public string member_id { get; set; } = "";
        public string patient_first { get; set; } = "";
        public string patient_last { get; set; } = "";
        public string patient_dob { get; set; } = "";
        public string patient_gender { get; set; } = "U";
        public string? prior_auth { get; set; }
        public string diagnoses { get; set; } = "";
        public string procedure { get; set; } = "";
        public string? modifiers { get; set; }
        public string units { get; set; } = "1";
        public string charge { get; set; } = "0";
        public string service_date { get; set; } = "";
        public string pos { get; set; } = "11";
        public string dx_pointers { get; set; } = "1";
    }

    public static IReadOnlyList<ParsedClaim> Parse(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = a => a.Header.Trim().ToLowerInvariant()
        };
        using var csv = new CsvReader(reader, config);
        var rows = csv.GetRecords<Row>().ToList();

        return rows.GroupBy(r => r.claim_ref).Select(g =>
        {
            try
            {
                var first = g.First();
                var lines = g.Select(r => new ServiceLineRequest(
                    r.procedure,
                    Split(r.modifiers),
                    decimal.Parse(r.units, CultureInfo.InvariantCulture),
                    decimal.Parse(r.charge, CultureInfo.InvariantCulture),
                    DateOnly.ParseExact(r.service_date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.pos.PadLeft(2, '0'),
                    Split(r.dx_pointers).Select(int.Parse).ToList())).ToList();

                var request = new ClaimRequest(
                    first.payer_id, first.billing_npi, string.IsNullOrWhiteSpace(first.rendering_npi) ? null : first.rendering_npi,
                    new PatientRequest(first.member_id, first.patient_first, first.patient_last,
                        DateOnly.ParseExact(first.patient_dob, "yyyy-MM-dd", CultureInfo.InvariantCulture), first.patient_gender),
                    Split(first.diagnoses), lines, string.IsNullOrWhiteSpace(first.prior_auth) ? null : first.prior_auth);
                return new ParsedClaim(g.Key, request, null);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                return new ParsedClaim(g.Key, null, $"Row format error: {ex.Message}");
            }
        }).ToList();
    }

    private static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
