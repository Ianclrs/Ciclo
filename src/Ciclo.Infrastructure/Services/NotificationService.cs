using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Data;
using Ciclo.Infrastructure.Email;
using Ciclo.Infrastructure.Security;

namespace Ciclo.Infrastructure.Services;

public interface INotificationService
{
    Task<NotificationDto> CreateAsync(CreateNotificationRequest request, Guid tenantId, UserRole role);
    Task<(int RecipientCount, NotificationDto? Notification)> BroadcastAsync(BroadcastNotificationRequest request, Guid tenantId, UserRole role);
    Task<int> SendByStudentAsync(Guid studentId, string titulo, string mensagem, NotificationType tipo, Guid? referenceId, Guid tenantId, UserRole role);
    Task<PagedResponse<NotificationDto>> GetForUserAsync(Guid userId, bool unreadOnly, int page, int pageSize);
    Task MarkReadAsync(Guid userNotificationId, Guid userId);
    Task<int> MarkAllReadAsync(Guid userId);
    Task<int> GetUnreadCountAsync(Guid userId);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailDispatcher _emailDispatcher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(AppDbContext db, IEmailDispatcher emailDispatcher, ILogger<NotificationService> logger)
    {
        _db = db;
        _emailDispatcher = emailDispatcher;
        _logger = logger;
    }

    // ── FR-001: Create Notification ──────────────────────────────────

    public async Task<NotificationDto> CreateAsync(CreateNotificationRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.NotificationWrite, role);

        if (string.IsNullOrWhiteSpace(request.Titulo) || request.Titulo.Length > 200)
            throw new NotificationException("titulo_required_max_200", 400);

        if (string.IsNullOrWhiteSpace(request.Mensagem) || request.Mensagem.Length > 2000)
            throw new NotificationException("mensagem_required_max_2000", 400);

        var userIds = request.UserIds;
        if (userIds is null || userIds.Count == 0)
            throw new NotificationException("user_ids_required", 400);

        // Validate all userIds belong to the same tenant
        var validUserIds = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id) && u.TenantId == tenantId)
            .Select(u => u.Id)
            .ToListAsync();

        if (validUserIds.Count != userIds.Count)
            throw new NotificationException("users_must_belong_to_same_tenant", 400);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Titulo = request.Titulo,
            Mensagem = request.Mensagem,
            Tipo = request.Tipo,
            ReferenceId = request.ReferenceId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Notifications.Add(notification);

        var userNotifications = validUserIds.Select(uid => new UserNotification
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            UserId = uid
        }).ToList();

        _db.UserNotifications.AddRange(userNotifications);
        await _db.SaveChangesAsync();

        // O e-mail sai por um dispatcher que roda FORA do escopo desta requisição
        // (ver EmailDispatchService): aqui só se entrega a mensagem. Não reverte a notificação
        // em caso de falha de envio — a notificação in-app é a fonte durável.
        await EnqueueEmailsAsync(validUserIds, request.Titulo, request.Mensagem);

        return MapToDto(notification, false);
    }

    // ── FR-002: Broadcast to Parents ─────────────────────────────────

    public async Task<(int RecipientCount, NotificationDto? Notification)> BroadcastAsync(
        BroadcastNotificationRequest request, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.NotificationBroadcast, role);

        if (string.IsNullOrWhiteSpace(request.Titulo) || request.Titulo.Length > 200)
            throw new NotificationException("titulo_required_max_200", 400);

        if (string.IsNullOrWhiteSpace(request.Mensagem) || request.Mensagem.Length > 2000)
            throw new NotificationException("mensagem_required_max_2000", 400);

        var parentIds = await _db.Users
            .Where(u => u.TenantId == tenantId && u.Role == UserRole.Parent && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        // B2: sem destinatários não existe notificação — devolver um DTO com Guid.Empty e
        // DateTime.MinValue era um objeto fantasma que o cliente poderia exibir.
        if (parentIds.Count == 0)
            return (0, null);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Titulo = request.Titulo,
            Mensagem = request.Mensagem,
            Tipo = request.Tipo,
            ReferenceId = request.ReferenceId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Notifications.Add(notification);

        var userNotifications = parentIds.Select(uid => new UserNotification
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            UserId = uid
        }).ToList();

        _db.UserNotifications.AddRange(userNotifications);

        await _db.SaveChangesAsync();

        await EnqueueEmailsAsync(parentIds, request.Titulo, request.Mensagem);

        return (parentIds.Count, MapToDto(notification, false));
    }

    // ── FR-003: Notify by Student ────────────────────────────────────

    public async Task<int> SendByStudentAsync(Guid studentId, string titulo, string mensagem,
        NotificationType tipo, Guid? referenceId, Guid tenantId, UserRole role)
    {
        AccessPolicy.Require(AccessPolicy.Roles.NotificationWrite, role);

        if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 200)
            throw new NotificationException("titulo_required_max_200", 400);

        if (string.IsNullOrWhiteSpace(mensagem) || mensagem.Length > 2000)
            throw new NotificationException("mensagem_required_max_2000", 400);

        // Verify student belongs to tenant (prevents cross-tenant data leak)
        var studentExists = await _db.Students
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == studentId && s.TenantId == tenantId);

        if (!studentExists)
            throw new NotificationException("student_not_found", 404);

        var parentIds = await _db.StudentParents
            .Where(sp => sp.StudentId == studentId)
            .Select(sp => sp.ParentId)
            .ToListAsync();

        // Aluno sem pais vinculados → count=0 (not an error)
        if (parentIds.Count == 0)
            return 0;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Titulo = titulo,
            Mensagem = mensagem,
            Tipo = tipo,
            ReferenceId = referenceId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Notifications.Add(notification);

        var userNotifications = parentIds.Select(uid => new UserNotification
        {
            Id = Guid.NewGuid(),
            NotificationId = notification.Id,
            UserId = uid
        }).ToList();

        _db.UserNotifications.AddRange(userNotifications);
        await _db.SaveChangesAsync();

        await EnqueueEmailsAsync(parentIds, titulo, mensagem);

        return parentIds.Count;
    }

    // ── FR-004: List User Notifications ──────────────────────────────

    public async Task<PagedResponse<NotificationDto>> GetForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);

        var query = _db.UserNotifications
            .Include(un => un.Notification)
            .Where(un => un.UserId == userId);

        if (unreadOnly)
            query = query.Where(un => !un.IsRead);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(un => un.Notification.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(un => new NotificationDto(
                un.Notification.Id,
                un.Id,
                un.Notification.Titulo,
                un.Notification.Mensagem,
                un.Notification.Tipo.ToString(),
                un.Notification.ReferenceId,
                un.IsRead,
                un.Notification.CreatedAt))
            .ToListAsync();

        return new PagedResponse<NotificationDto>(items, total, page, pageSize);
    }

    // ── FR-005: Mark Single as Read ──────────────────────────────────

    public async Task MarkReadAsync(Guid userNotificationId, Guid userId)
    {
        var un = await _db.UserNotifications
            .FirstOrDefaultAsync(u => u.Id == userNotificationId && u.UserId == userId);

        if (un is null)
            throw new NotificationException("notification_not_found", 404);

        // Already read → idempotent
        if (!un.IsRead)
        {
            un.IsRead = true;
            un.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    // ── FR-006: Mark All as Read ─────────────────────────────────────

    public async Task<int> MarkAllReadAsync(Guid userId)
    {
        var unread = await _db.UserNotifications
            .Where(un => un.UserId == userId && !un.IsRead)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var un in unread)
        {
            un.IsRead = true;
            un.ReadAt = now;
        }

        if (unread.Count > 0)
            await _db.SaveChangesAsync();

        return unread.Count;
    }

    // ── FR-007: Unread Count ─────────────────────────────────────────

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await _db.UserNotifications
            .CountAsync(un => un.UserId == userId && !un.IsRead);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static NotificationDto MapToDto(Notification n, bool isRead)
    {
        return new NotificationDto(n.Id, Guid.Empty, n.Titulo, n.Mensagem, n.Tipo.ToString(), n.ReferenceId, isRead, n.CreatedAt);
    }

    private async Task EnqueueEmailsAsync(List<Guid> userIds, string titulo, string mensagem)
    {
        try
        {
            // A consulta acontece AQUI, dentro do escopo da requisição — antes ela rodava numa task
            // descartada, depois da resposta, num AppDbContext possivelmente já descartado.
            var users = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.Name })
                .ToListAsync();

            foreach (var user in users)
            {
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    _emailDispatcher.Enqueue(
                        new EmailMessage(user.Email, user.Name, titulo, mensagem));
                }
            }
        }
        catch (Exception ex)
        {
            // Não relança de propósito: a notificação in-app já foi persistida (o SaveChanges
            // aconteceu antes). Falhar a requisição aqui faria o cliente achar que nada foi criado
            // e repetir a operação, gerando notificação duplicada. Mas loga como ERRO, e não
            // warning: o e-mail não vai sair para ninguém neste lote.
#pragma warning disable CA1848, CA1873
            _logger.LogError(ex, "Failed to enqueue email notifications");
#pragma warning restore CA1848, CA1873
        }
    }
}

public class NotificationException : Exception
{
    public int StatusCode { get; }
    public NotificationException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
