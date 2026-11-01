using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using YeaKazi.BuildingBlocks.Domain;

namespace YeaKazi.BuildingBlocks.Audit;

/// <summary>
/// Produces masked before/after snapshots for audit events. Properties marked
/// [AuditIgnore] are dropped; properties marked [AuditMask] are partially masked.
/// </summary>
public static class AuditSnapshot
{
    public const string MaskToken = "***";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { ApplyAuditAttributes },
        },
    };

    public static JsonDocument? Of(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.SerializeToDocument(value, value.GetType(), Options);
    }

    /// <summary>Masks all but the last few characters: "A123456789Z" -> "***89Z".</summary>
    public static string? Mask(string? value, int visibleSuffix = 3)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length <= visibleSuffix)
        {
            return MaskToken;
        }

        return MaskToken + value[^visibleSuffix..];
    }

    /// <summary>Masks an email local part: "jane.doe@acme.co.ke" -> "j***@acme.co.ke".</summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return email;
        }

        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return MaskToken;
        }

        return email[0] + MaskToken + email[at..];
    }

    private static void ApplyAuditAttributes(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var property = typeInfo.Properties[i];
            var member = property.AttributeProvider as MemberInfo;
            if (member is null)
            {
                continue;
            }

            if (member.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
            {
                typeInfo.Properties.RemoveAt(i);
                continue;
            }

            if (member.GetCustomAttribute<AuditMaskAttribute>() is not null)
            {
                var getter = property.Get;
                if (getter is null)
                {
                    continue;
                }

                var masked = typeInfo.CreateJsonPropertyInfo(typeof(string), property.Name);
                masked.Get = obj =>
                {
                    var raw = getter(obj);
                    if (raw is null)
                    {
                        return null;
                    }

                    var text = raw.ToString();
                    return text is not null && text.Contains('@') ? MaskEmail(text) : Mask(text);
                };
                typeInfo.Properties[i] = masked;
            }
        }
    }
}
