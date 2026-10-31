using System.Text.Json;
using Microsoft.Extensions.Options;
using YeaKazi.BuildingBlocks.Security;
using YeaKazi.Platform.Auth;
using YeaKazi.Platform.Users;
using YeaKazi.Workers.Outbox;

namespace YeaKazi.Workers.Email;

/// <summary>Sends the account activation link for iam.user_invited.</summary>
public sealed class InvitationEmailHandler(IEmailSender sender, IFieldEncryptor encryptor, IOptions<AuthOptions> auth) : IOutboxHandler
{
    public string Type => OutboxTypes.UserInvited;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken ct)
    {
        var invite = message.Payload.Deserialize<InvitationMessage>(JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException("Malformed invitation message.");
        var token = encryptor.Decrypt(invite.EncryptedToken);
        var link = $"{auth.Value.PortalBaseUrl.TrimEnd('/')}/activate?tenant={Uri.EscapeDataString(invite.TenantSlug)}&token={Uri.EscapeDataString(token)}";
        var body = $"""
            Hello {invite.DisplayName},

            You have been invited to {invite.TenantName} on YeaKazi.
            Set your password using this link before {invite.ExpiresAt:yyyy-MM-dd HH:mm} UTC:

            {link}

            If you did not expect this invitation, ignore this email.
            YeaKazi by Yeasoft
            """;
        await sender.SendAsync(new EmailMessage(invite.Email, "Your YeaKazi invitation", body), ct);
    }
}
