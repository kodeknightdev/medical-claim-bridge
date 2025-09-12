using ClaimBridge.Contracts.Reference;
using ClaimBridge.Providers.Data;
using ClaimBridge.ServiceDefaults.Phi;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Providers.Seeding;

public static class ProvidersSeeder
{
    public static async Task SeedAsync(ProvidersDb db, IPhiProtector protector, IConfiguration config, TimeProvider clock)
    {
        foreach (var payer in PayerDirectory.All)
            if (!await db.Payers.AnyAsync(p => p.Id == payer.Id))
                db.Payers.Add(new Payer { Id = payer.Id, Name = payer.Name, Channel = payer.Channel, TimelyFilingDays = payer.TimelyFilingDays });

        if (await db.Organizations.AnyAsync())
        {
            await db.SaveChangesAsync();
            return;
        }

        var now = clock.GetUtcNow();
        // NPIs below pass the Luhn check but are made up; tax ids sit in the never-issued 00- range
        db.Organizations.AddRange(
            new ProviderOrganization
            {
                Id = SeedIds.SunriseOrg, Name = "Sunrise Family Clinic", GroupNpi = "1999000015", TaxId = "00-0001001",
                Taxonomy = "207Q00000X", AddressLine1 = "100 Demo Way", City = "Springfield", State = "IL", Zip = "62701",
                ContactEmail = "billing@sunriseclinic.local", CreatedAt = now,
                Providers =
                [
                    new() { Id = Guid.NewGuid(), Npi = "1999000114", FirstName = "Avery", LastName = "Demo", Taxonomy = "207Q00000X" },
                    new() { Id = Guid.NewGuid(), Npi = "1999000122", FirstName = "Jordan", LastName = "Sample", Taxonomy = "207R00000X" }
                ]
            },
            new ProviderOrganization
            {
                Id = SeedIds.LakesideOrg, Name = "Lakeside Orthopedics", GroupNpi = "1999000023", TaxId = "00-0001002",
                Taxonomy = "207X00000X", AddressLine1 = "200 Example Ave", City = "Lakeview", State = "WI", Zip = "53001",
                ContactEmail = "billing@lakesideortho.local", CreatedAt = now,
                Providers =
                [
                    new() { Id = Guid.NewGuid(), Npi = "1999000213", FirstName = "Riley", LastName = "Placeholder", Taxonomy = "207X00000X" },
                    new() { Id = Guid.NewGuid(), Npi = "1999000221", FirstName = "Casey", LastName = "Fictional", Taxonomy = "207XS0114X" }
                ]
            },
            new ProviderOrganization
            {
                Id = SeedIds.HarborOrg, Name = "Harbor Pediatrics", GroupNpi = "1999000031", TaxId = "00-0001003",
                Taxonomy = "208000000X", AddressLine1 = "300 Sample St", City = "Bayport", State = "ME", Zip = "04001",
                ContactEmail = "billing@harborpeds.local", CreatedAt = now,
                Providers =
                [
                    new() { Id = Guid.NewGuid(), Npi = "1999000312", FirstName = "Morgan", LastName = "Testcase", Taxonomy = "208000000X" },
                    new() { Id = Guid.NewGuid(), Npi = "1999000320", FirstName = "Quinn", LastName = "Mockup", Taxonomy = "208000000X" }
                ]
            });

        (Guid Org, string Payer, EnrollmentStatus Status)[] enrollments =
        [
            (SeedIds.SunriseOrg, SeedIds.PayerA, EnrollmentStatus.Active),
            (SeedIds.SunriseOrg, SeedIds.PayerB, EnrollmentStatus.Active),
            (SeedIds.SunriseOrg, SeedIds.PayerC, EnrollmentStatus.Active),
            (SeedIds.LakesideOrg, SeedIds.PayerA, EnrollmentStatus.Active),
            (SeedIds.LakesideOrg, SeedIds.PayerB, EnrollmentStatus.Active),
            (SeedIds.LakesideOrg, SeedIds.PayerC, EnrollmentStatus.Pending),
            (SeedIds.HarborOrg, SeedIds.PayerA, EnrollmentStatus.Active),
            (SeedIds.HarborOrg, SeedIds.PayerB, EnrollmentStatus.Rejected),
            (SeedIds.HarborOrg, SeedIds.PayerC, EnrollmentStatus.Active),
        ];
        foreach (var (org, payer, status) in enrollments)
            db.Enrollments.Add(new PayerEnrollment
            {
                Id = Guid.NewGuid(), OrgId = org, PayerId = payer, Status = status, RequestedAt = now.AddDays(-400),
                RequestedBy = "seed", DecidedAt = status == EnrollmentStatus.Pending ? null : now.AddDays(-395),
                DecidedBy = status == EnrollmentStatus.Pending ? null : "seed",
                Note = status == EnrollmentStatus.Rejected ? "Missing W-9 on file" : null
            });

        var webhookSecret = config["Seed:WebhookSecret"];
        if (!string.IsNullOrWhiteSpace(webhookSecret))
            db.Webhooks.Add(new WebhookSubscription
            {
                Id = Guid.Parse("b2000000-0000-0000-0000-000000000001"), OrgId = SeedIds.SunriseOrg,
                Url = config["Seed:WebhookUrl"] ?? "http://localhost:5190/webhooks/sunrise",
                Events = "claim.accepted,claim.rejected,claim.paid,claim.denied,remittance.posted",
                SigningSecretProtected = protector.Protect(webhookSecret), CreatedAt = now
            });

        await db.SaveChangesAsync();
    }
}
