namespace Chirograph.Infrastructure.Email;

public enum EmailDelivery
{
    /// <summary>Write messages as .eml files to a local folder (development). Nothing leaves the machine.</summary>
    Outbox,

    /// <summary>Send through an SMTP server.</summary>
    Smtp,
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    public EmailDelivery Delivery { get; set; } = EmailDelivery.Outbox;

    public string FromAddress { get; set; } = "no-reply@chirograph.local";

    public string FromName { get; set; } = "Chirograph";

    /// <summary>Folder for <see cref="EmailDelivery.Outbox"/>. Relative paths are resolved against the content root.</summary>
    public string OutboxDirectory { get; set; } = "App_Data/outbox";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    /// <summary>Supply via user-secrets or the <c>Email__Smtp__Password</c> environment variable, never appsettings.</summary>
    public string? Password { get; set; }

    /// <summary>MailKit SecureSocketOptions name: Auto, StartTls, SslOnConnect or None.</summary>
    public string Security { get; set; } = "StartTls";
}
