using System.Text.Json;
using System.Xml.Linq;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;

namespace Copeland.Cli;

internal static partial class TsclBuildContract
{
    private static void PublishBrowserNuget(string stagingDirectory, CSharpNugetWorker worker,
        NugetWorkerConfiguration config, string delivery, string runtimeAssembly)
    {
        bool wasm = delivery == "wasm";
        string projectDirectory = stagingDirectory + ".nuget-project";
        string hostDirectory = Path.Combine(stagingDirectory, NugetWorkerFolder);
        Directory.CreateDirectory(projectDirectory);
        try
        {
            WriteStagedFile(projectDirectory, "Dispatch.cs", worker.Source);
            WriteStagedFile(projectDirectory, "Program.cs", wasm ? BrowserWasmProgram : BrowserServiceProgram);
            WriteStagedFile(projectDirectory, "NugetWorker.csproj", CreateBrowserNugetProject(config, runtimeAssembly, wasm));
            string publish = wasm ? Path.Combine(projectDirectory, "publish") : Path.Combine(hostDirectory, "service");
            RunDotNetPublish(projectDirectory, "NugetWorker.csproj", publish, wasm
                ? CopelandBackendTarget.Create("csharp", "wasm")
                : CopelandBackendTarget.Create("csharp", "ryujit"));
            if (wasm)
            {
                string framework = Path.Combine(publish, "wwwroot", "_framework");
                if (!File.Exists(Path.Combine(framework, "dotnet.js")))
                {
                    throw new TsclContractException("COPE-TSCL-NUGET-0007", "The NuGet WASM worker publish is missing dotnet.js.");
                }
                Directory.CreateDirectory(hostDirectory);
                Directory.Move(framework, Path.Combine(hostDirectory, "_framework"));
                WriteStagedFile(hostDirectory, "wasm-worker.mjs", ReadNugetResource("NugetBrowserWasm.mjs"));
            }
            string transportModule = wasm ? "NugetBrowserWorkerTransport.mjs" : "NugetBrowserServiceTransport.mjs";
            WriteStagedFile(hostDirectory, "transport.mjs", ReadNugetResource(transportModule));
            WriteStagedFile(hostDirectory, "host.mjs",
                $"const digest = {JsonSerializer.Serialize(worker.Digest)};\n" + ReadNugetResource("NugetWire.mjs")
                + "\n" + ReadNugetResource("NugetBrowserHost.mjs"));
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }

    private static string CreateBrowserNugetProject(NugetWorkerConfiguration config, string runtimeAssembly, bool wasm)
    {
        var properties = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net10.0"),
            new XElement("Nullable", "enable"),
            new XElement("ImplicitUsings", wasm ? "disable" : "enable"),
            new XElement("TreatWarningsAsErrors", "true"),
            new XElement("ManagePackageVersionsCentrally", "false"));
        if (wasm)
        {
            properties.Add(new XElement("OutputType", "Exe"),
                new XElement("AllowUnsafeBlocks", "true"),
                new XElement("TrimmerSingleWarn", "false"));
        }
        else
        {
            properties.Add(new XElement("UseAppHost", "false"));
        }
        var references = NugetPackageReferences(config.Packages);
        references.Add(new XElement("Reference", new XAttribute("Include", "Copeland.TS.Sidecar"),
            new XElement("HintPath", runtimeAssembly)));
        return new XElement("Project", new XAttribute("Sdk", wasm ? "Microsoft.NET.Sdk.BlazorWebAssembly" : "Microsoft.NET.Sdk.Web"),
            properties, references).ToString();
    }

    private const string BrowserWasmProgram = """
        using System.Runtime.InteropServices.JavaScript;
        using System.Runtime.Versioning;

        namespace Copeland.Generated;

        [SupportedOSPlatform("browser")]
        public static partial class NugetWasm
        {
            public static void Main() {}

            [JSExport]
            public static string Invoke(string digest, string request)
                => NugetWorker.Invoke(digest, request) ?? "";
        }
        """;

    private const string BrowserServiceProgram = """
        using System.Text;
        using Copeland.Generated;
        using Copeland.TS.Sidecar;

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = SidecarWire.MaximumFrameBytes);
        var app = builder.Build();
        app.MapPost("/copeland-nuget/invoke", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.Headers.TryGetValue("X-Copeland-Contract", out var digest) || digest.Count != 1)
            {
                return Results.BadRequest();
            }
            if (context.Request.ContentLength > SidecarWire.MaximumFrameBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            string request;
            try
            {
                using var reader = new StreamReader(context.Request.Body, new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                request = await reader.ReadToEndAsync(context.RequestAborted);
            }
            catch (DecoderFallbackException)
            {
                return Results.BadRequest();
            }
            string? response = NugetWorker.Invoke(digest.ToString(), request);
            return response is null ? Results.BadRequest() : Results.Text(response, "text/plain", Encoding.UTF8);
        });
        app.Run();
        """;
}
