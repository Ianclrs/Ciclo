using Ciclo.Core.Entities;
using Ciclo.Infrastructure.Contracts;
using Ciclo.Infrastructure.Email;
using Ciclo.Infrastructure.Services;
using Ciclo.Infrastructure.Storage;

namespace Ciclo.Api.Tests;

/// <summary>
/// Dublês sem comportamento usados quando o teste exercita um caminho que NÃO deve chegar ao
/// armazenamento, ao envio de e-mail ou à notificação. Compartilhados porque dois arquivos de
/// teste precisam deles — cópias separadas divergiriam na primeira mudança de interface.
/// </summary>
internal sealed class NoopStorage : IFileStorage
{
    public List<string> Deleted { get; } = [];

    public Task<string> SaveAsync(Guid tenantId, Guid studentId, string fileName, Stream content) =>
        Task.FromResult($"{tenantId}/{studentId}/{fileName}");

    public Task<Stream> GetAsync(string filePath) =>
        Task.FromResult<Stream>(Stream.Null);

    public Task DeleteAsync(string filePath)
    {
        Deleted.Add(filePath);
        return Task.CompletedTask;
    }

    public Task ReencryptAsync(string filePath) => Task.CompletedTask;
}

internal sealed class NoopNotifications : INotificationService
{
    public Task<NotificationDto> CreateAsync(CreateNotificationRequest request, Guid tenantId, UserRole role) =>
        throw new NotSupportedException("Não usado neste teste.");

    public Task<(int RecipientCount, NotificationDto? Notification)> BroadcastAsync(
        BroadcastNotificationRequest request, Guid tenantId, UserRole role) =>
        throw new NotSupportedException("Não usado neste teste.");

    public Task<int> SendByStudentAsync(
        Guid studentId, string titulo, string mensagem,
        NotificationType tipo, Guid? referenceId, Guid tenantId, UserRole role) => Task.FromResult(0);

    public Task<PagedResponse<NotificationDto>> GetForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize) =>
        Task.FromResult(new PagedResponse<NotificationDto>([], 0, page, pageSize));

    public Task MarkReadAsync(Guid userNotificationId, Guid userId) => Task.CompletedTask;

    public Task<int> MarkAllReadAsync(Guid userId) => Task.FromResult(0);

    public Task<int> GetUnreadCountAsync(Guid userId) => Task.FromResult(0);
}

internal sealed class NoopEmailDispatcher : IEmailDispatcher
{
    public void Enqueue(EmailMessage message) { }
}
