using Copeland.React.Copeland;
using System.Diagnostics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/greeting", (string? name) => new { greeting = Greeter.Greeting(name ?? "Copeland") });

app.Lifetime.ApplicationStarted.Register(() =>
{
    if (Environment.GetEnvironmentVariable("COPLAND_DISABLE_BROWSER") == "1")
    {
        return;
    }

    string address = app.Urls.FirstOrDefault() ?? "http://localhost:5137";
    Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
});

app.Run();
