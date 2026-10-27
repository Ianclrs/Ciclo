namespace Ciclo.Core.Auditing;

public static class AuditActions
{
    public const string EnrollmentApproved = "enrollment.approved";
    public const string EnrollmentRejected = "enrollment.rejected";
    public const string EnrollmentCancelled = "enrollment.cancelled";
    public const string DocumentVerified = "document.verified";
    public const string DocumentRejected = "document.rejected";
    public const string SessionRevoked = "session.revoked";
}
