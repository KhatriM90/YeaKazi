using System.Data;
using System.Data.Common;
using Npgsql;
using YeaKazi.BuildingBlocks.Tenancy;

namespace YeaKazi.BuildingBlocks.Persistence;

/// <summary>
/// One PostgreSQL connection per request/job scope, shared by every module DbContext,
/// so a unit of work that spans modules (e.g. tenant provisioning) commits atomically.
/// Every time the connection opens, the tenant session variables are applied; Npgsql
/// resets session state (DISCARD ALL) when a pooled connection is reused.
/// </summary>
public sealed class SharedDbConnection : IAsyncDisposable, IDisposable
{
    private readonly RequestContext _context;

    public SharedDbConnection(NpgsqlDataSource dataSource, RequestContext context)
    {
        _context = context;
        Connection = dataSource.CreateConnection();
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction? CurrentTransaction { get; private set; }

    public async Task<SharedTransaction> BeginTransactionAsync(
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        if (CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A shared transaction is already active.");
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(ct);
            await TenantSession.ApplyAsync(Connection, _context, null, ct);
        }

        CurrentTransaction = await Connection.BeginTransactionAsync(isolation, ct);
        return new SharedTransaction(this, CurrentTransaction);
    }

    /// <summary>Re-applies the tenant session variables to the open connection (used after the context changes mid-transaction).</summary>
    public Task ApplySessionAsync(CancellationToken ct = default)
        => TenantSession.ApplyAsync(Connection, _context, CurrentTransaction, ct);

    internal void Clear() => CurrentTransaction = null;

    public async ValueTask DisposeAsync()
    {
        if (CurrentTransaction is not null)
        {
            await CurrentTransaction.DisposeAsync();
            CurrentTransaction = null;
        }

        await Connection.DisposeAsync();
    }

    public void Dispose()
    {
        CurrentTransaction?.Dispose();
        CurrentTransaction = null;
        Connection.Dispose();
    }
}

public sealed class SharedTransaction : IAsyncDisposable
{
    private readonly SharedDbConnection _owner;
    private readonly NpgsqlTransaction _transaction;
    private bool _completed;

    internal SharedTransaction(SharedDbConnection owner, NpgsqlTransaction transaction)
    {
        _owner = owner;
        _transaction = transaction;
    }

    public DbTransaction Transaction => _transaction;

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction.CommitAsync(ct);
        _completed = true;
        _owner.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await _transaction.RollbackAsync();
            _owner.Clear();
        }

        await _transaction.DisposeAsync();
    }
}

public static class TenantSession
{
    public const string TenantSetting = "app.tenant_id";
    public const string ScopeSetting = "app.scope";

    public static async Task ApplyAsync(DbConnection connection, RequestContext context, DbTransaction? transaction, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "select set_config('app.tenant_id', @tenant, false), set_config('app.scope', @scope, false)";
        var tenant = cmd.CreateParameter();
        tenant.ParameterName = "tenant";
        tenant.Value = context.TenantId?.ToString() ?? string.Empty;
        cmd.Parameters.Add(tenant);
        var scope = cmd.CreateParameter();
        scope.ParameterName = "scope";
        scope.Value = context.IsPlatformScope ? "platform" : (context.TenantId is null ? string.Empty : "tenant");
        cmd.Parameters.Add(scope);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static void Apply(DbConnection connection, RequestContext context)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select set_config('app.tenant_id', @tenant, false), set_config('app.scope', @scope, false)";
        var tenant = cmd.CreateParameter();
        tenant.ParameterName = "tenant";
        tenant.Value = context.TenantId?.ToString() ?? string.Empty;
        cmd.Parameters.Add(tenant);
        var scope = cmd.CreateParameter();
        scope.ParameterName = "scope";
        scope.Value = context.IsPlatformScope ? "platform" : (context.TenantId is null ? string.Empty : "tenant");
        cmd.Parameters.Add(scope);
        cmd.ExecuteNonQuery();
    }
}
