namespace Ciclo.Core.Auditing;

public static class AuditActions
{
    public const string EnrollmentApproved = "enrollment.approved";
    public const string EnrollmentRejected = "enrollment.rejected";
    public const string EnrollmentCancelled = "enrollment.cancelled";
    public const string DocumentVerified = "document.verified";
    public const string DocumentRejected = "document.rejected";
    public const string SessionRevoked = "session.revoked";

    // Privacidade e LGPD (Spec 210): a trilha de acesso a dados sensíveis reutiliza o
    // AuditEvent da spec 110, sem tabela paralela.
    public const string ConsentGranted = "privacy.consent.granted";
    public const string ConsentRevoked = "privacy.consent.revoked";
    public const string PrivacyExported = "privacy.exported";
    public const string PrivacyAnonymized = "privacy.anonymized";
    public const string PrivacyRequestDecided = "privacy.request.decided";
}
