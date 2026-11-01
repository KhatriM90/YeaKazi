using Microsoft.EntityFrameworkCore;
using YeaKazi.BuildingBlocks.Audit;
using YeaKazi.BuildingBlocks.Outbox;

namespace YeaKazi.BuildingBlocks.Persistence;

/// <summary>
/// Mapping of the cross-module tables (tenant audit log and outbox). The Platform
/// module's migrations own these tables; other modules map them without migrating.
/// </summary>
public static class SharedModel
{
    public const string AuditSchema = "audit";
    public const string MessagingSchema = "messaging";

    public static void ConfigureAudit(ModelBuilder modelBuilder, bool ownsTables)
    {
        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("audit_events", AuditSchema, t =>
            {
                if (!ownsTables)
                {
                    t.ExcludeFromMigrations();
                }
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.ActorKind).HasMaxLength(20).IsRequired();
            b.Property(x => x.ActorDisplay).HasMaxLength(200);
            b.Property(x => x.Action).HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            b.Property(x => x.EntityId).HasMaxLength(100);
            b.Property(x => x.Reason).HasMaxLength(1000);
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.ClientIp).HasMaxLength(64);
            b.Property(x => x.BeforeData).HasColumnType("jsonb");
            b.Property(x => x.AfterData).HasColumnType("jsonb");
            b.Property(x => x.Seq).ValueGeneratedOnAdd().HasDefaultValue(0L);
            b.Property(x => x.PrevHash).HasMaxLength(64).ValueGeneratedOnAdd().HasDefaultValue(string.Empty);
            b.Property(x => x.Hash).HasMaxLength(64).ValueGeneratedOnAdd().HasDefaultValue(string.Empty);
            b.HasIndex(x => new { x.TenantId, x.Seq }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.OccurredAt });
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            b.HasIndex(x => new { x.TenantId, x.ActorUserId });
        });
    }

    public static void ConfigureOutbox(ModelBuilder modelBuilder, bool ownsTables)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", MessagingSchema, t =>
            {
                if (!ownsTables)
                {
                    t.ExcludeFromMigrations();
                }
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(100).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.LastError).HasMaxLength(2000);
            b.HasIndex(x => new { x.NextAttemptAt })
                .HasFilter("processed_at is null and dead_lettered_at is null")
                .HasDatabaseName("ix_outbox_messages_pending");
        });
    }
}
