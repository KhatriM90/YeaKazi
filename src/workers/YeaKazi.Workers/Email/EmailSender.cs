using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YeaKazi.Workers.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>Email channel (NOT-01). Message bodies never contain salary values or other secrets beyond one-time links.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class FileDropOptions
{
    public const string Section = "Email:FileDrop";
    public string Directory { get; set; } = "./.maildrop";
}

/// <summary>Development-only sender: writes .eml files to a local folder. A real provider arrives with S8 (NOT-01).</summary>
public sealed class FileDropEmailSender(IOptions<FileDropOptions> options, ILogger<FileDropEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var dir = Path.GetFullPath(options.Value.Directory);
        System.IO.Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
        var eml = new StringBuilder()
            .AppendLine($"To: {message.To}")
            .AppendLine("From: YeaKazi <no-reply@yeakazi.local>")
            .AppendLine($"Subject: {message.Subject}")
            .AppendLine("Content-Type: text/plain; charset=utf-8")
            .AppendLine()
            .AppendLine(message.TextBody)
            .ToString();
        await File.WriteAllTextAsync(path, eml, ct);
        logger.LogInformation("Email written to {Path}", path);
    }
}
