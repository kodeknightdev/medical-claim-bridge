using ClaimBridge.Contracts.Events;
using ClaimBridge.Providers.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Providers.Controllers;

public class EnrollmentService(ProvidersDb db, IEventOutbox outbox, TimeProvider clock)
{
    public async Task<PayerEnrollment?> RequestAsync(Guid orgId, string payerId, string actor)
    {
        var existing = await db.Enrollments.FirstOrDefaultAsync(e => e.OrgId == orgId && e.PayerId == payerId);
        if (existing is { Status: not EnrollmentStatus.Rejected }) return null;

        // a rejected enrollment can be re-requested; it goes back to Pending
        var enrollment = existing ?? new PayerEnrollment { Id = Guid.NewGuid(), OrgId = orgId, PayerId = payerId };
        enrollment.Status = EnrollmentStatus.Pending;
        enrollment.RequestedAt = clock.GetUtcNow();
        enrollment.RequestedBy = actor;
        enrollment.DecidedAt = null;
        enrollment.DecidedBy = null;
        if (existing is null) db.Enrollments.Add(enrollment);

        Audit(actor, "enrollment.requested", $"{orgId}/{payerId}");
        Publish(enrollment);
        await db.SaveChangesAsync();
        return enrollment;
    }

    public async Task DecideAsync(PayerEnrollment enrollment, EnrollmentStatus status, string? note, string actor)
    {
        enrollment.Status = status;
        enrollment.Note = note;
        enrollment.DecidedAt = clock.GetUtcNow();
        enrollment.DecidedBy = actor;
        Audit(actor, "enrollment." + status.ToString().ToLowerInvariant(), $"{enrollment.OrgId}/{enrollment.PayerId}");
        Publish(enrollment);
        await db.SaveChangesAsync();
    }

    public Task<bool> IsActiveAsync(Guid orgId, string payerId, CancellationToken ct = default) =>
        db.Enrollments.AnyAsync(e => e.OrgId == orgId && e.PayerId == payerId && e.Status == EnrollmentStatus.Active, ct);

    private void Publish(PayerEnrollment e) =>
        outbox.Enqueue(Topics.EnrollmentChanged, e.OrgId.ToString(), new EnrollmentChanged(e.OrgId, e.PayerId, e.Status.ToString()));

    private void Audit(string actor, string action, string target) =>
        db.AuditLog.Add(new AuditLogEntry { At = clock.GetUtcNow(), Actor = actor, Action = action, Target = target });
}
