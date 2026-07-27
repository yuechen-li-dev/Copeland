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
app.Use(async (context, next) =>
{
    await next();
    if (context.Request.Path.StartsWithSegments("/__copeland/m0/bridge", StringComparison.Ordinal))
    {
        Console.WriteLine($"COPELAND_BRIDGE_REQUEST path={context.Request.Path} status={context.Response.StatusCode}");
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
GeneratedCopelandBridgeEndpoints.Map(app);
app.MapFallbackToFile("index.html");

await app.StartAsync();
Console.WriteLine($"COPELAND_BRIDGE_READY {app.Urls.Single()}");
await app.WaitForShutdownAsync();
