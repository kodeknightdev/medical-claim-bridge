using System.Globalization;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Remittance.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClaimBridge.Remittance.Pdf;

/// <summary>Human-readable remittance advice, one section per claim with adjustments and their explanations.</summary>
public static class RemittancePdf
{
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    public static byte[] Render(RemittanceFile file, IReadOnlyList<Posting> postings, IReadOnlyDictionary<(string Kind, string Code), string> codes,
        string? audienceLabel)
    {
        var payer = PayerDirectory.Find(file.PayerId)?.Name ?? file.PayerId;
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Remittance advice").FontSize(16).SemiBold();
                        c.Item().Text($"{payer}  ·  EFT {file.EftTraceNumber ?? "n/a"}  ·  paid {file.PaymentDate:yyyy-MM-dd}");
                        if (audienceLabel is not null) c.Item().Text(audienceLabel).FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(150).AlignRight().Text($"Total paid {postings.Sum(p => p.Paid).ToString("C", Money)}").SemiBold();
                });
                col.Item().PaddingTop(4).Text("claim-bridge demo · synthetic data · simplified 835, not a payer document")
                    .FontSize(7).FontColor(Colors.Grey.Medium);
                col.Item().PaddingVertical(6).LineHorizontal(0.5f);
            });

            page.Content().Column(col =>
            {
                foreach (var p in postings)
                {
                    col.Item().PaddingTop(8).Text($"Claim {p.ClaimNumber}  ·  payer ref {p.PayerClaimId ?? "-"}  ·  {Label(p.Kind)}").SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        foreach (var h in new[] { "Billed", "Allowed", "Paid", "Patient owes", "Write-off" })
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(h).SemiBold();
                        foreach (var v in new[] { p.Charge, p.Allowed, p.Paid, p.PatientResponsibility, p.ContractualAdjustments + p.OtherAdjustments })
                            table.Cell().Padding(3).Text(v.ToString("C", Money));
                    });

                    foreach (var a in p.Adjustments)
                        col.Item().PaddingLeft(8).Text(text =>
                        {
                            text.Span($"{a.GroupCode}-{a.ReasonCode}  {a.Amount.ToString("C", Money)}  ").SemiBold();
                            text.Span(codes.GetValueOrDefault(("CARC", a.ReasonCode)) ?? "");
                        });
                    foreach (var r in p.RemarkCodes)
                        col.Item().PaddingLeft(8).Text($"Remark {r}: {codes.GetValueOrDefault(("RARC", r))}").Italic();
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static string Label(string kind) => kind switch
    {
        "Payment" => "paid",
        "PartialPayment" => "partially paid",
        "Denial" => "denied",
        "Reversal" => "reversal / takeback",
        _ => kind
    };
}
