using System.Text.Json;
using YeaKazi.Platform.Auth;
using YeaKazi.Workers.Outbox;

namespace YeaKazi.Workers.Email;

/// <summary>Tells a user about a security-relevant change to their sign-in (iam.security_notice). No codes or secrets are sent.</summary>
public sealed class SecurityNoticeEmailHandler(IEmailSender sender) : IOutboxHandler
{
    public string Type => SecurityNoticeKinds.OutboxType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken ct)
    {
        var notice = message.Payload.Deserialize<SecurityNoticeMessage>(JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException("Malformed security notice.");
        var what = notice.Kind switch
        {
            SecurityNoticeKinds.RecoveryCodeUsed => "A recovery code was used to sign in to your account. Generate new recovery codes from your security settings.",
            SecurityNoticeKinds.RecoveryCodesRegenerated => "New MFA recovery codes were generated for your account. Your old codes no longer work.",
            SecurityNoticeKinds.MfaReset => "An administrator reset multi-factor sign-in on your account. You will be asked to set it up again at your next sign-in.",
            _ => "A security setting on your account changed.",
        };
        var body = $"""
            Hello {notice.DisplayName},

            {what}

            Organization: {notice.TenantName}
            Time: {notice.OccurredAt:yyyy-MM-dd HH:mm} UTC

            If this was not you, contact your administrator immediately.
            YeaKazi by Yeasoft
            """;
        await sender.SendAsync(new EmailMessage(notice.Email, "YeaKazi security notice", body), ct);
    }
}
