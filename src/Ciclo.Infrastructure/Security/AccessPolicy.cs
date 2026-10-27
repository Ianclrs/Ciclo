using Ciclo.Core.Entities;
using Ciclo.Core.Exceptions;

namespace Ciclo.Infrastructure.Security;

/// <summary>
/// Dono único da regra "quem pode o quê" (M4).
///
/// Antes, a autorização de escrita existia apenas no atributo <c>[Authorize]</c> do
/// controller: qualquer chamador do serviço (outro serviço, um job, um teste) executava
/// a operação sem checar papel. A leitura, por outro lado, recebia <c>userId</c>/<c>role</c>
/// e checava dentro do serviço — a assimetria deixava a proteção dependente de o chamador
/// lembrar do atributo.
///
/// Agora a regra tem um dono só: as constantes abaixo alimentam tanto o atributo do
/// controller (primeiro portão, deny-by-default) quanto a checagem dentro do serviço
/// (segundo portão, independente do chamador). Um papel fora da lista vira
/// <see cref="ForbiddenException"/>, traduzida pelo <c>ExceptionMiddleware</c> em
/// 403 <c>forbidden</c> no formato canônico da API.
/// </summary>
public static class AccessPolicy
{
    /// <summary>
    /// Listas de papéis em CSV — o mesmo formato aceito por <c>[Authorize(Roles = ...)]</c>,
    /// para que o atributo e a checagem de serviço leiam a mesma definição.
    /// </summary>
    public static class Roles
    {
        public const string StudentRead = "Admin,Staff,Parent";
        public const string StudentWrite = "Admin,Staff";
        public const string StudentDelete = "Admin";

        public const string DocumentRead = "Admin,Staff,Parent";
        public const string DocumentStaffRead = "Admin,Staff";
        public const string DocumentUpload = "Admin,Staff,Parent";
        public const string DocumentVerify = "Admin,Staff";
        public const string DocumentTypeManage = "Admin";

        public const string EnrollmentPeriodRead = "Admin,Staff";
        public const string EnrollmentPeriodManage = "Admin";
        public const string EnrollmentRead = "Admin,Staff,Parent";
        public const string EnrollmentWrite = "Admin,Staff";
        public const string EnrollmentReview = "Admin,Staff";
        public const string EnrollmentCancel = "Admin";
        public const string EnrollmentReopen = "Admin";

        // Auditoria (Spec 110): consulta e exportacao sao restritas ao Admin.
        public const string AuditRead = "Admin";

        public const string NotificationWrite = "Admin,Staff";
        public const string NotificationBroadcast = "Admin";

        public const string ParentOnly = "Parent";

        public const string UserRead = "Admin";
        public const string UserManage = "Admin";
    }

    /// <summary>Indica se o papel está na lista, sem lançar.</summary>
    public static bool Allows(string allowedRoles, UserRole role)
    {
        foreach (var allowed in allowedRoles.Split(','))
        {
            if (string.Equals(allowed.Trim(), role.ToString(), StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Exige que o papel esteja na lista. Lançar aqui é o que torna a regra
    /// independente do chamador: não há caminho que execute a operação sem passar por esta checagem.
    /// </summary>
    public static void Require(string allowedRoles, UserRole role)
    {
        if (!Allows(allowedRoles, role))
            throw new ForbiddenException("forbidden");
    }
}
