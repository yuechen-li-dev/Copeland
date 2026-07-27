using Copeland.Generated.Bridge;

WebApplicationOptions options = new()
{
    Args = args,
    ApplicationName = typeof(Program).Assembly.GetName().Name,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
};
WebApplicationBuilder builder = WebApplication.CreateBuilder(options);
builder.WebHost.UseUrls("http://127.0.0.1:0");

WebApplication app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
GeneratedCopelandBridgeEndpoints.Map(app);
app.MapFallbackToFile("index.html");

await app.StartAsync();
Console.WriteLine($"COPELAND_BRIDGE_READY {app.Urls.Single()}");
await app.WaitForShutdownAsync();
