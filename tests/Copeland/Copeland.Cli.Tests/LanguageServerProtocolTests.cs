using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Copeland.TS.LanguageServer;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed class LanguageServerProtocolTests
{
    [Fact]
    public void Language_server_uses_manifest_project_context_for_react_stream_overlays()
    {
        using var workspace = new TempWorkspace();
        const string sourceText = "import { createElement } from \"react\"; function Hero(): ReactNode { return createElement(); } stream Site<0px, 0px> { width: 320px; height: 180px; hero: Hero() { height: fill; } }";
        string sourcePath = workspace.Write("src/App.tsx", sourceText);
        workspace.Write("manifest.tsx", "export default manifest({});");
        workspace.Write(".tspack/build-manifests/site-browser.request.json", $$"""
            {
              "projectRoot": "{{workspace.Path.Replace("\\", "\\\\")}}",
              "sources": [
                { "logicalPath": "src/App.tsx", "path": "{{sourcePath.Replace("\\", "\\\\")}}" }
              ],
              "javascriptRuntime": "browser",
              "tsXmlProfile": "react-m0",
              "npmContracts": [
                {
                  "packageName": "react",
                  "version": "19.2.7",
                  "materialized": true,
                  "exports": [
                    { "name": "createElement", "parameters": [], "result": "ReactNode" }
                  ],
                  "components": []
                }
              ]
            }
            """);

        using var client = new LspClient();
        string sourceUri = VsCodeFileUri(sourcePath);
        client.Request(1, "initialize", new { initializationOptions = new { workspaceRoot = workspace.Path } });
        client.Notify("textDocument/didOpen", new { textDocument = new { uri = sourceUri, version = 1, text = sourceText } });
        JsonElement diagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(diagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        JsonElement hover = client.Request(2, "textDocument/hover", new
        {
            textDocument = new { uri = sourceUri },
            position = new { line = 0, character = sourceText.IndexOf("Site", StringComparison.Ordinal) + 1 },
        });
        Assert.Contains("origin: (0px, 0px)", hover.GetProperty("contents").GetProperty("value").GetString());

        const string invalidOverlay = "import { createElement } from \"react\"; function Hero(): ReactNode { return createElement(); } stream Site { width: 320px; height: 180px; hero: Hero() { height: fill; } }";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 2 }, contentChanges = new[] { new { text = invalidOverlay } } });
        JsonElement invalidDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Contains(invalidDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "COPE-STREAM-0001");
    }

    [Fact]
    public void Language_server_uses_generated_ownership_and_compiles_open_buffers()
    {
        using var workspace = new TempWorkspace();
        const string appText = "import { Score } from \"./Library\"; import { transform } from \"@fixture/transform\"; function Main(): number { return Score(transform(1)); }";
        const string reactText = "import { createElement } from \"react\"; export function View(): ReactNode { return <main />; }";
        string libraryPath = workspace.Write("src/copeland/Library.ts", "export function Score(value: number): number { return value; }");
        string sourcePath = workspace.Write("src/copeland/App.ts", appText);
        string reactPath = workspace.Write("src/copeland/View.tsx", reactText);
        string tscPath = workspace.Write("src/legacy/Legacy.ts", "const normal = true;");
        string npmContractPath = workspace.Write("contracts/transform.json", """
            {
              "schemaVersion": 1,
              "package": "@fixture/transform",
              "version": "1.0.0",
              "materialization": "node_modules/@fixture/transform/index.js",
              "materialized": true,
              "exports": [{ "name": "transform", "parameters": ["number"], "result": "number" }]
            }
            """);
        string reactContractPath = workspace.Write("contracts/react.json", """
            {
              "schemaVersion": 1,
              "package": "react",
              "version": "19.2.7",
              "materialized": true,
              "exports": [{ "name": "createElement", "parameters": [], "result": "ReactNode" }]
            }
            """);
        workspace.Write("obj/copeland/workspace/editor-ownership.generated.json", """
            { "schemaVersion": 1, "workspaceRoot": ".", "files": [
              { "path": "src/copeland/App.ts", "owner": "tscl", "project": "App.csproj", "matchedRule": "src/copeland/**" },
              { "path": "src/copeland/Library.ts", "owner": "tscl", "project": "App.csproj", "matchedRule": "src/copeland/**" },
              { "path": "src/copeland/View.tsx", "owner": "tscl", "project": "App.csproj", "matchedRule": "src/copeland/**" },
              { "path": "src/legacy/Legacy.ts", "owner": "tsc", "project": "generated", "matchedRule": "src/legacy/**" }
            ] }
            """);
        string targets = Path.Combine(FindRepositoryRoot(), "src", "Copeland", "Copeland.TS.MSBuild", "build", "Copeland.TS.Sdk.targets");
        string taskAssembly = typeof(Copeland.TS.MSBuild.CopelandCompile).Assembly.Location;
        string projectPath = workspace.Write("App.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
                <CopelandTsXmlProfile>react-m0</CopelandTsXmlProfile>
              </PropertyGroup>
              <ItemGroup>
                <CopelandCompile Include="src/copeland/App.ts" />
                <CopelandCompile Include="src/copeland/Library.ts" />
                <CopelandCompile Include="src/copeland/View.tsx" />
                <CopelandNpmContract Include="{{npmContractPath}}" />
                <CopelandNpmContract Include="{{reactContractPath}}" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """);
        Restore(projectPath);
        using var client = new LspClient();
        string sourceUri = VsCodeFileUri(sourcePath);
        JsonElement initialized = client.Request(1, "initialize", new { initializationOptions = new { workspaceRoot = workspace.Path } });
        Assert.True(initialized.GetProperty("capabilities").GetProperty("hoverProvider").GetBoolean());
        Assert.Equal(1, initialized.GetProperty("capabilities").GetProperty("textDocumentSync").GetInt32());

        client.Notify("textDocument/didOpen", new { textDocument = new { uri = sourceUri, version = 1, text = appText } });
        JsonElement diagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(diagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        client.Notify("textDocument/didOpen", new { textDocument = new { uri = new Uri(reactPath).AbsoluteUri, version = 1, text = reactText } });
        JsonElement reactDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(reactDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        JsonElement completion = client.Request(2, "textDocument/completion", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = 0 } });
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Score");
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "transform");

        JsonElement definition = client.Request(3, "textDocument/definition", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = appText.LastIndexOf("Score", StringComparison.Ordinal) + 1 } });
        Assert.Equal(new Uri(libraryPath).AbsoluteUri, definition.GetProperty("uri").GetString());

        JsonElement npmHover = client.Request(4, "textDocument/hover", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = appText.LastIndexOf("transform", StringComparison.Ordinal) + 1 } });
        Assert.Contains("npm function transform", npmHover.GetProperty("contents").GetProperty("value").GetString());

        JsonElement npmDefinition = client.Request(5, "textDocument/definition", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = appText.LastIndexOf("transform", StringComparison.Ordinal) + 1 } });
        Assert.Equal(new Uri(npmContractPath).AbsoluteUri, npmDefinition.GetProperty("uri").GetString());

        const string layoutText = "layout Shell<12px, 8px> { width: 320px; height: 160px; slot content { frame: { x: 0px, y: 0px, width: 320px, height: 160px }; } }";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 2 }, contentChanges = new[] { new { text = layoutText } } });
        JsonElement layoutDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(layoutDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        JsonElement layoutHover = client.Request(6, "textDocument/hover", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = layoutText.IndexOf("Shell", StringComparison.Ordinal) + 1 } });
        Assert.Contains("origin: (12px, 8px)", layoutHover.GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("normalized boxes:", layoutHover.GetProperty("contents").GetProperty("value").GetString());

        JsonElement layoutTokens = client.Request(7, "textDocument/semanticTokens/full", new { textDocument = new { uri = sourceUri } });
        Assert.NotEmpty(layoutTokens.GetProperty("data").EnumerateArray());

        const string missingOrigin = "layout Draft";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 3 }, contentChanges = new[] { new { text = missingOrigin } } });
        JsonElement missingOriginDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Contains(missingOriginDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "COPE-LAYOUT-ORIGIN-0001");

        JsonElement originCompletion = client.Request(8, "textDocument/completion", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = missingOrigin.Length } });
        Assert.Contains(originCompletion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "<0px, 0px>");

        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 4 }, contentChanges = new[] { new { text = layoutText } } });
        JsonElement clearedOriginDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(clearedOriginDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        const string layoutTypeText = "layout type ShellContract { row root { column left; column right; } } layout Typed<0px, 0px> satisfies ShellContract { width: 320px; height: 160px; row root { column left { width: 80px; height: fill; } column right { width: fill; height: fill; } } }";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 5 }, contentChanges = new[] { new { text = layoutTypeText } } });
        JsonElement layoutTypeDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(layoutTypeDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        JsonElement layoutTypeHover = client.Request(9, "textDocument/hover", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = layoutTypeText.IndexOf("ShellContract", StringComparison.Ordinal) + 1 } });
        Assert.Contains("layout type ShellContract", layoutTypeHover.GetProperty("contents").GetProperty("value").GetString());
        JsonElement layoutTypeCompletion = client.Request(10, "textDocument/completion", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = layoutTypeText.IndexOf("satisfies", StringComparison.Ordinal) + 1 } });
        Assert.Contains(layoutTypeCompletion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "ShellContract");
        JsonElement layoutTypeSymbols = client.Request(11, "textDocument/documentSymbol", new { textDocument = new { uri = sourceUri } });
        Assert.Contains(layoutTypeSymbols.EnumerateArray(), symbol => symbol.GetProperty("name").GetString() == "ShellContract");

        const string missingChild = "layout type ShellContract { row root { column left; column right; } } layout Typed<0px, 0px> satisfies ShellContract { width: 320px; height: 160px; row root { column left { width: 80px; height: fill; } } }";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 6 }, contentChanges = new[] { new { text = missingChild } } });
        JsonElement missingChildDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Contains(missingChildDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == "COPE-LAYOUT-TYPE-0012");

        const string streamText = "import { createElement } from \"react\"; function Header(): ReactNode { return createElement(); } function Content(): ReactNode { return createElement(); } stream Page<0px, 0px> { width: 320px; height: 160px; header: Header() { height: 32px; } content: Content() { height: fill; } }";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 7 }, contentChanges = new[] { new { text = streamText } } });
        JsonElement streamDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(streamDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        JsonElement streamHover = client.Request(12, "textDocument/hover", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = streamText.IndexOf("Page", StringComparison.Ordinal) + 1 } });
        Assert.Contains("origin: (0px, 0px)", streamHover.GetProperty("contents").GetProperty("value").GetString());
        JsonElement streamSymbols = client.Request(13, "textDocument/documentSymbol", new { textDocument = new { uri = sourceUri } });
        Assert.Contains(streamSymbols.EnumerateArray(), symbol => symbol.GetProperty("name").GetString() == "Page");
        JsonElement streamTokens = client.Request(14, "textDocument/semanticTokens/full", new { textDocument = new { uri = sourceUri } });
        Assert.NotEmpty(streamTokens.GetProperty("data").EnumerateArray());
        JsonElement streamCompletion = client.Request(15, "textDocument/completion", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = 0 } });
        Assert.Contains(streamCompletion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "stream");

        const string relativeLayoutText = "layout Dialog<0px, 0px> { width: 600px; height: 400px; overlay root { width: 600px; height: 400px; slot dialog { width: 200px; height: 100px; } with centerIn(root); } }";
        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 8 }, contentChanges = new[] { new { text = relativeLayoutText } } });
        JsonElement relativeDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(relativeDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        JsonElement relativeCompletion = client.Request(16, "textDocument/completion", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = relativeLayoutText.IndexOf("centerIn", StringComparison.Ordinal) + 1 } });
        Assert.Contains(relativeCompletion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "centerIn");
        JsonElement relativeHover = client.Request(17, "textDocument/hover", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = relativeLayoutText.IndexOf("centerIn", StringComparison.Ordinal) + 1 } });
        Assert.Contains("reads: source.x", relativeHover.GetProperty("contents").GetProperty("value").GetString());
        JsonElement relativeDefinition = client.Request(18, "textDocument/definition", new { textDocument = new { uri = sourceUri }, position = new { line = 0, character = relativeLayoutText.LastIndexOf("root", StringComparison.Ordinal) + 1 } });
        Assert.Equal(sourceUri, relativeDefinition.GetProperty("uri").GetString());

        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 9 }, contentChanges = new[] { new { text = "import { Score } from \"./Library\"; function Main(): number { return ; }" } } });
        JsonElement changedDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.NotEmpty(changedDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        client.Notify("textDocument/didChange", new { textDocument = new { uri = sourceUri, version = 10 }, contentChanges = new[] { new { text = "function Main(" } } });
        JsonElement syntaxDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.NotEmpty(syntaxDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());

        client.Notify("textDocument/didOpen", new { textDocument = new { uri = new Uri(tscPath).AbsoluteUri, version = 1, text = "const normal = true;" } });
        JsonElement tscDiagnostics = client.ReadNotification("textDocument/publishDiagnostics");
        Assert.Empty(tscDiagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
    }

    private static string VsCodeFileUri(string path)
    {
        string uri = new Uri(path).AbsoluteUri;
        if (!OperatingSystem.IsWindows()) return uri;

        string drive = Path.GetPathRoot(path)!.TrimEnd(Path.DirectorySeparatorChar);
        return uri.Replace(drive, Uri.EscapeDataString(drive.ToLowerInvariant()), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LspClient : IDisposable
    {
        private readonly Process _process;

        public LspClient()
        {
            _process = Process.Start(new ProcessStartInfo("dotnet", '"' + typeof(LanguageServerHost).Assembly.Location + '"')
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
        }

        public JsonElement Request(int id, string method, object parameters)
        {
            Send(new { jsonrpc = "2.0", id, method, @params = parameters });
            using JsonDocument response = Read();
            return response.RootElement.GetProperty("result").Clone();
        }

        public void Notify(string method, object parameters) => Send(new { jsonrpc = "2.0", method, @params = parameters });
        public JsonElement ReadNotification(string method)
        {
            using JsonDocument notification = Read();
            Assert.Equal(method, notification.RootElement.GetProperty("method").GetString());
            return notification.RootElement.Clone();
        }

        private void Send(object message)
        {
            string json = JsonSerializer.Serialize(message);
            _process.StandardInput.Write("Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json);
            _process.StandardInput.Flush();
        }

        private JsonDocument Read()
        {
            string? header = _process.StandardOutput.ReadLine();
            Assert.NotNull(header);
            int length = int.Parse(header!["Content-Length: ".Length..]);
            Assert.Equal(string.Empty, _process.StandardOutput.ReadLine());
            char[] characters = new char[length];
            int offset = 0;
            while (offset < length)
            {
                int count = _process.StandardOutput.Read(characters, offset, length - offset);
                Assert.True(count > 0);
                offset += count;
            }
            return JsonDocument.Parse(new string(characters));
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }
            _process.Dispose();
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "copeland-lsp-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public string Write(string relativePath, string text)
        {
            string path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void Restore(string projectPath)
    {
        using Process process = Process.Start(new ProcessStartInfo("dotnet", "restore \"" + projectPath + "\"") { UseShellExecute = false })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
