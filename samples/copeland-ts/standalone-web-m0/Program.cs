using System.Diagnostics;
using System.Text.Json;
using Copeland.Generated.Bridge;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;

StandaloneHostOptions hostOptions = StandaloneHostOptions.Parse(args);
string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
GeneratedAssetManifest.Validate(webRoot);

WebApplicationOptions applicationOptions = new()
{
    Args = args,
    ApplicationName = typeof(Program).Assembly.GetName().Name,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = webRoot,
};
WebApplicationBuilder builder = WebApplication.CreateBuilder(applicationOptions);
builder.WebHost.UseUrls(hostOptions.Url);

WebApplication app = builder.Build();
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new PhysicalFileProvider(webRoot),
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRoot),
    ServeUnknownFileTypes = false,
});
GeneratedCopelandBridgeEndpoints.Map(app);
app.MapFallbackToFile("index.html");

await app.StartAsync();
string address = GetListeningAddress(app);
Console.WriteLine($"Application is running at {address}");
Console.WriteLine($"COPELAND_STANDALONE_READY {address}");
if (hostOptions.IsPublicBinding)
{
    Console.WriteLine("WARNING: The application is listening on a non-loopback address. Configure normal production security before exposing it publicly.");
}

if (hostOptions.OpenBrowser)
{
    if (!DefaultBrowserLauncher.TryOpen(address, out string? error))
    {
        Console.WriteLine("The default browser could not be opened automatically.");
        Console.WriteLine($"Browser launch detail: {error}");
    }
}

await app.WaitForShutdownAsync();

static string GetListeningAddress(WebApplication app)
{
    IServer server = app.Services.GetRequiredService<IServer>();
    IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
    string? address = addresses?.Addresses.SingleOrDefault(candidate => candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    return address ?? app.Urls.Single();
}

sealed record StandaloneHostOptions(string Url, bool OpenBrowser, bool IsPublicBinding)
{
    public static StandaloneHostOptions Parse(IReadOnlyList<string> args)
    {
        bool noBrowser = args.Contains("--no-browser", StringComparer.Ordinal);
        bool openBrowser = args.Contains("--open-browser", StringComparer.Ordinal) || !noBrowser;
        string? explicitUrls = GetValue(args, "--urls") ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(explicitUrls))
        {
            return new StandaloneHostOptions(explicitUrls, openBrowser, ContainsPublicAddress(explicitUrls));
        }

        string host = GetValue(args, "--host") ?? "127.0.0.1";
        string port = GetValue(args, "--port") ?? "0";
        if (!int.TryParse(port, out int parsedPort) || parsedPort is < 0 or > 65535)
        {
            throw new ArgumentException("--port must be an integer from 0 through 65535.");
        }

        return new StandaloneHostOptions($"http://{host}:{parsedPort}", openBrowser, !IsLoopbackHost(host));
    }

    private static string? GetValue(IReadOnlyList<string> args, string name)
    {
        for (int index = 0; index < args.Count - 1; index += 1)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal)) return args[index + 1];
        }

        return null;
    }

    private static bool ContainsPublicAddress(string urls)
        => urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.Host : string.Empty)
            .Any(host => !IsLoopbackHost(host));

    private static bool IsLoopbackHost(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal);
}

static class GeneratedAssetManifest
{
    private static readonly string[] RequiredAssets =
    [
        "index.html",
        "browser-materialization.json",
        "import-map.json",
        "bridge-config.js",
        "bridge-contract.json",
        "Bridge.js",
        "Main.js",
    ];

    public static void Validate(string webRoot)
    {
        foreach (string asset in RequiredAssets)
        {
            string path = Path.Combine(webRoot, asset);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"COPE-HOST-0001: Generated browser application assets are missing ({asset}). Run `dotnet build` before launching this application.");
            }
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(File.ReadAllText(Path.Combine(webRoot, "browser-materialization.json")));
            using JsonDocument __ = JsonDocument.Parse(File.ReadAllText(Path.Combine(webRoot, "import-map.json")));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"COPE-HOST-0002: Generated browser asset manifest is invalid: {exception.Message}", exception);
        }
    }
}

static class DefaultBrowserLauncher
{
    public static bool TryOpen(string url, out string? error)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("COPELAND_BROWSER_LAUNCHER"), "record", StringComparison.Ordinal))
        {
            Console.WriteLine($"COPELAND_BROWSER_OPEN {url}");
            error = null;
            return true;
        }

        try
        {
            ProcessStartInfo startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo { FileName = url, UseShellExecute = true }
                : OperatingSystem.IsMacOS()
                    ? CreateCommand("open", url)
                    : CreateCommand("xdg-open", url);
            Process.Start(startInfo);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static ProcessStartInfo CreateCommand(string command, string url)
    {
        var startInfo = new ProcessStartInfo(command) { UseShellExecute = false };
        startInfo.ArgumentList.Add(url);
        return startInfo;
    }
}
