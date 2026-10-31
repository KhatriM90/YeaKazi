using YeaKazi.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddYeaKaziApi();
var app = builder.Build();
app.UseYeaKaziApi();

if (args.Length > 0 && await CommandLine.TryRunAsync(app, args))
{
    return;
}

app.Run();

public partial class Program;
