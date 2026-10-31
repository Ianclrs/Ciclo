using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Ciclo.Infrastructure.Email;

/// <summary>
/// Provedor real de e-mail sobre o contrato existente <see cref="IEmailSender"/>.
///
/// Fail-closed na construcao: <c>Host</c>, <c>Porta</c> e <c>Remetente</c> sao obrigatorios. Uma
/// implantacao mal configurada falha no boot (validacao do service provider) em vez de degradar
/// para um no-op silencioso — que e exatamente o que o sender de console esconde.
///
/// Falhas de SMTP propagam sem captura: o chamador (o dispatcher proativo) registra
/// <c>Falhou</c> no registro de entrega. Nada e engolido aqui.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    /// <summary>Prefixo da falha de configuracao — verificavel por quem opera o deploy.</summary>
    public const string MissingConfigurationCode = "email_configuration_missing";

    private readonly string _host;
    private readonly int _porta;
    private readonly string _remetente;
    private readonly string? _usuario;
    private readonly string? _senha;

    public SmtpEmailSender(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var host = configuration["Email:Smtp:Host"];
        var portaTexto = configuration["Email:Smtp:Porta"];
        var remetente = configuration["Email:Smtp:Remetente"];

        var ausentes = new List<string>();

        if (string.IsNullOrWhiteSpace(host))
        {
            ausentes.Add("Email:Smtp:Host");
        }

        if (string.IsNullOrWhiteSpace(remetente))
        {
            ausentes.Add("Email:Smtp:Remetente");
        }

        if (!int.TryParse(portaTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var porta) || porta <= 0)
        {
            ausentes.Add("Email:Smtp:Porta");
        }

        if (ausentes.Count > 0)
        {
            throw new InvalidOperationException(
                $"{MissingConfigurationCode}: missing or invalid configuration key(s): {string.Join(", ", ausentes)}");
        }

        _host = host!;
        _remetente = remetente!;
        _porta = porta;
        _usuario = configuration["Email:Smtp:Usuario"];
        _senha = configuration["Email:Smtp:Senha"];
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string body)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_remetente),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        message.To.Add(new MailAddress(toEmail, toName));

        using var client = new SmtpClient(_host, _porta);

        // Credencial opcional: servidores locais de relay aceitam envio anonimo.
        if (!string.IsNullOrWhiteSpace(_usuario))
        {
            client.Credentials = new NetworkCredential(_usuario, _senha);
        }

        await client.SendMailAsync(message).ConfigureAwait(false);
    }
}
