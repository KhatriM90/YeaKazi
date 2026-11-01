using Npgsql;
using YeaKazi.Platform;
using YeaKazi.Platform.Auth;
using YeaKazi.Workers.Email;
using YeaKazi.Workers.Outbox;

var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

builder.Services.AddSingleton(_ => new WorkerDataSource(NpgsqlDataSource.Create(
    config.GetConnectionString("Worker") ?? throw new InvalidOperationException("ConnectionStrings:Worker is not configured."))));
builder.Services.Configure<OutboxOptions>(config.GetSection(OutboxOptions.Section));
builder.Services.Configure<FileDropOptions>(config.GetSection(FileDropOptions.Section));
builder.Services.Configure<AuthOptions>(config.GetSection(AuthOptions.Section));
builder.Services.AddFieldEncryption(config);
builder.Services.AddSingleton<IEmailSender, FileDropEmailSender>();
builder.Services.AddSingleton<IOutboxHandler, InvitationEmailHandler>();
builder.Services.AddSingleton<IOutboxHandler, SecurityNoticeEmailHandler>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxWorker>();

builder.Build().Run();
