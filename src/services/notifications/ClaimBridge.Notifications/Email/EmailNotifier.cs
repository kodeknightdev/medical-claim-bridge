using ClaimBridge.Contracts.Events;
using ClaimBridge.Notifications.Data;
using ClaimBridge.Notifications.Webhooks;
using MailKit.Net.Smtp;
using MimeKit;

namespace ClaimBridge.Notifications.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "no-reply@claimbridge.local";
}

/// <summary>Short heads-up emails to the practice's billing contact; MailHog catches them locally.</summary>
public class EmailNotifier(NotificationsDb db, ProvidersClient providers, Microsoft.Extensions.Options.IOptions<SmtpOptions> smtp,
    TimeProvider clock, ILogger<EmailNotifier> logger)
{
    public async Task QueueClaimProblemAsync(ClaimStatusChanged e, string type, CancellationToken ct)
    {
        var org = await providers.OrgAsync(e.OrgId, ct);
        if (org is null || string.IsNullOrWhiteSpace(org.ContactEmail)) return;

        var subject = type == WebhookEvents.ClaimDenied
            ? $"Claim {e.ClaimNumber} was denied by {e.PayerId}"
            : $"Claim {e.ClaimNumber} was rejected by {e.PayerId}";
        var body = $"""
            Hello {org.Name} billing team,

            {subject}.
            Reason: {e.Reason ?? "see the claim timeline"}{(e.CarcCode is null ? "" : $" (CARC {e.CarcCode})")}
            Billed amount: {e.TotalCharge:0.00}

            Open the denial worklist in claim-bridge to correct or appeal it.
            """;

        var log = new EmailLog { OrgId = e.OrgId, To = org.ContactEmail, Subject = subject, Template = type, At = clock.GetUtcNow() };
        db.Emails.Add(log);
        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(smtp.Value.From));
            message.To.Add(MailboxAddress.Parse(org.ContactEmail));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Value.Host, smtp.Value.Port, MailKit.Security.SecureSocketOptions.None, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            log.Sent = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // email is best effort; the webhook and the worklist are the system of record
            log.Error = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            logger.LogWarning("Email to org {OrgId} failed: {Error}", e.OrgId, ex.Message);
        }
    }
}
