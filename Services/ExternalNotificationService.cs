using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;

namespace InventoryManager.Services;

public sealed class ExternalNotificationService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task SendTestEmailAsync(CancellationToken cancellationToken = default)
    {
        var host = Required("INVENTORY_SMTP_HOST");
        var port = int.TryParse(Environment.GetEnvironmentVariable("INVENTORY_SMTP_PORT"), out var smtpPort) ? smtpPort : 587;
        var username = Required("INVENTORY_SMTP_USER");
        var password = Required("INVENTORY_SMTP_PASSWORD");
        var from = Required("INVENTORY_SMTP_FROM");
        var recipient = Required("INVENTORY_ALERT_EMAIL");
        using var message = new MailMessage(from, recipient, "Inventory Manager notification test",
            $"Email notifications are configured. Test sent at {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
#pragma warning disable SYSLIB0014
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
#pragma warning restore SYSLIB0014
    }

    public async Task SendTestWhatsAppAsync(CancellationToken cancellationToken = default)
    {
        var phoneNumberId = Required("INVENTORY_WHATSAPP_PHONE_NUMBER_ID");
        var accessToken = Required("INVENTORY_WHATSAPP_ACCESS_TOKEN");
        var recipient = Required("INVENTORY_WHATSAPP_TO");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/v21.0/{Uri.EscapeDataString(phoneNumberId)}/messages");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = recipient,
            type = "text",
            text = new { body = $"Inventory Manager notification test at {DateTime.Now:yyyy-MM-dd HH:mm:ss}." }
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Set the {name} environment variable before using this integration.")
            : value.Trim();
    }
}
