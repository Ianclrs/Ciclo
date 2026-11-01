namespace Ciclo.Core.Auditing;

public static class AuditTargetTypes
{
    public const string Enrollment = "Enrollment";
    public const string Document = "Document";
    public const string Session = "Session";

    // Privacidade e LGPD (Spec 210).
    public const string Student = "Student";
    public const string PrivacyRequest = "PrivacyRequest";
}
