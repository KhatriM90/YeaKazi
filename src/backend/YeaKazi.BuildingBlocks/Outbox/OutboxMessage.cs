using YeaKazi.BuildingBlocks.Domain;

namespace YeaKazi.BuildingBlocks.Outbox;

/// <summary>
/// Transactional outbox row. Written in the same transaction as the business change
/// and dispatched by the worker host. Payloads must never contain salary values or
/// unencrypted secrets.
/// </summary>
public sealed class OutboxMessage : ITenantOwned, IAuditExempt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
    public string? LastError { get; set; }
}
