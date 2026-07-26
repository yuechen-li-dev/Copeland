using System.Reflection;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Manifest;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class SidecarProcessInteropTests
{
    [Fact]
    public async Task Real_Node_sidecar_completes_typed_calls_out_of_order_and_ignores_duplicates()
    {
        await using Fixture fixture = await Fixture.CreateAsync("normal");
        object first = GeneratedModuleInvoker.Invoke(fixture.Assembly, "call", "double", 1d)!;
        object second = GeneratedModuleInvoker.Invoke(fixture.Assembly, "call", "double", 2d)!;

        await WaitForCompletion(first);
        await WaitForCompletion(second);

        Assert.True(IsCompleted(first));
        Assert.True(IsCompleted(second));
        Assert.True(IsOk(GetValue(first)), Status(first));
        Assert.True(IsOk(GetValue(second)), Status(second));
        Assert.Equal(42d, ReadNumberResult(first));
        Assert.Equal(42d, ReadNumberResult(second));
    }

    [Fact]
    public async Task Real_Node_sidecar_preserves_remote_failure_cancellation_and_connection_failure()
    {
        await using (Fixture remote = await Fixture.CreateAsync("normal"))
        {
            object result = GeneratedModuleInvoker.Invoke(remote.Assembly, "call", "remote", 1d)!;
            await WaitForCompletion(result);
            Assert.False(IsOk(GetValue(result)));
        }

        await using (Fixture cancelled = await Fixture.CreateAsync("normal"))
        {
            object result = GeneratedModuleInvoker.Invoke(cancelled.Assembly, "call", "cancel", 1d)!;
            await WaitForCompletion(result);
            Assert.True((bool)result.GetType().GetProperty("IsCancelled")!.GetValue(result)!);
        }

        await using (Fixture lost = await Fixture.CreateAsync("normal"))
        {
            object result = GeneratedModuleInvoker.Invoke(lost.Assembly, "call", "lose", 1d)!;
            await WaitForCompletion(result);
            Assert.True((bool)result.GetType().GetProperty("IsTransportFailed")!.GetValue(result)!);
        }
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("mismatch")]
    public async Task Malformed_frames_and_handshake_mismatch_fail_the_live_correlation(string mode)
    {
        await using Fixture fixture = await Fixture.CreateAsync(mode);
        string operation = mode == "malformed" ? "malformed" : "double";
        object result = GeneratedModuleInvoker.Invoke(fixture.Assembly, "call", operation, 1d)!;
        await WaitForCompletion(result);
        Assert.True((bool)result.GetType().GetProperty("IsTransportFailed")!.GetValue(result)!);
    }

    private static bool IsCompleted(object computation)
        => (bool)computation.GetType().GetProperty("IsCompleted")!.GetValue(computation)!;

    private static object GetValue(object computation)
        => computation.GetType().GetProperty("Value")!.GetValue(computation)!;

    private static bool IsOk(object result)
        => (bool)result.GetType().GetProperty("IsOk")!.GetValue(result)!;

    private static string Status(object computation)
        => string.Join(", ", new[] { "IsCancelled", "IsPanicked", "IsTransportFailed" }.Select(name => name + "=" + computation.GetType().GetProperty(name)!.GetValue(computation)));

    private static double ReadNumberResult(object computation)
    {
        object response = GetValue(computation).GetType().GetProperty("Value")!.GetValue(GetValue(computation))!;
        return (double)response.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(property => property.PropertyType == typeof(double)).GetValue(response)!;
    }

    private static async Task WaitForCompletion(object computation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!IsCompleted(computation) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(IsCompleted(computation), "The sidecar computation did not settle within five seconds.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly CSharpSidecarHost _host;

        private Fixture(string root, Assembly assembly, CSharpSidecarHost host)
        {
            _root = root;
            Assembly = assembly;
            _host = host;
        }

        public Assembly Assembly { get; }

        public static async Task<Fixture> CreateAsync(string mode)
        {
            string root = Path.Combine(Path.GetTempPath(), "Copeland.Sidecar.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "sidecar"));
            File.WriteAllText(Path.Combine(root, "manifest.tsx"), """
                import { Package, RunTargets, Sidecars, Workspace, define } from "tspack/manifest";
                export default define(<Workspace name="sample"><Package name="app" version="1" kind="app"><RunTargets rows={[{ name: "node", runtime: "node", cwd: "workspace", command: ["sidecar/fixture.js"] }]} /></Package><Sidecars rows={[{ id: "node-transport", runTarget: "sample/app/node", default: true }]} /></Workspace>);
                """);

            ManifestProjectLoadResult manifestResult = CopelandProject.LoadRootManifest(root);
            Assert.True(manifestResult.Success, string.Join(Environment.NewLine, manifestResult.Diagnostics));
            CopelandCompilation source = CopelandCompiler.CompileToMir(Source);
            Assert.True(source.Success, string.Join(Environment.NewLine, source.Diagnostics));
            CSharpCompilation emitted = CSharpBackend.EmitForRootManifest(source.MirCompilation!.Program!, manifestResult.Manifest!);
            Assert.Empty(emitted.Diagnostics);
            RoslynCompileResult compiled = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));

            string success = Encode(compiled.Assembly!, response: true);
            string remote = Encode(compiled.Assembly!, response: false);
            Assert.NotNull(Decode(compiled.Assembly!, success, response: true));
            Assert.NotNull(Decode(compiled.Assembly!, remote, response: false));
            File.WriteAllText(Path.Combine(root, "sidecar", "fixture.js"), Script(mode, success, remote));
            var host = CSharpSidecarHost.Attach(compiled.Assembly!, emitted.SidecarContract!);
            await Task.CompletedTask;
            return new Fixture(root, compiled.Assembly!, host);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }

        private static string Encode(Assembly assembly, bool response)
        {
            Type record = assembly.GetTypes().Single(type =>
            {
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
                return type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
                    && (response
                        ? properties.Length == 2 && properties.Any(property => property.PropertyType == typeof(double)) && properties.Any(property => property.PropertyType == typeof(string))
                        : properties.Length == 1 && properties[0].PropertyType == typeof(string));
            });
            object value = response
                ? Activator.CreateInstance(record, BindingFlags.Instance | BindingFlags.NonPublic, null, [42d, "ok"], null)!
                : Activator.CreateInstance(record, BindingFlags.Instance | BindingFlags.NonPublic, null, ["remote"], null)!;
            MethodInfo encoder = assembly.GetType("Copeland.Generated.CopelandModule")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name.StartsWith("__tson_encode_", StringComparison.Ordinal)
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == record);
            object encoded = encoder.Invoke(null, [value])!;
            return (string)encoded.GetType().GetProperty("Value")!.GetValue(encoded)!;
        }

        private static object? Decode(Assembly assembly, string payload, bool response)
        {
            Type record = assembly.GetTypes().Single(type =>
            {
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
                return type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
                    && (response
                        ? properties.Length == 2 && properties.Any(property => property.PropertyType == typeof(double)) && properties.Any(property => property.PropertyType == typeof(string))
                        : properties.Length == 1 && properties[0].PropertyType == typeof(string));
            });
            MethodInfo decoder = assembly.GetType("Copeland.Generated.CopelandModule")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name.StartsWith("__tson_decode_", StringComparison.Ordinal)
                    && method.ReturnType == record);
            return decoder.Invoke(null, [payload]);
        }

        private static string Script(string mode, string success, string remote)
        {
            string success64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(success));
            string remote64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(remote));
            return $$"""
                const mode = {{Quote(mode)}};
                const success = Buffer.from({{Quote(success64)}}, "base64").toString("utf8");
                const remote = Buffer.from({{Quote(remote64)}}, "base64").toString("utf8");
                function esc(value) { return value.replaceAll("\\", "\\\\").replaceAll("\"", "\\\"").replaceAll("\n", "\\n").replaceAll("\r", "\\r").replaceAll("\t", "\\t"); }
                function envelope(correlation, kind, operation, payload) {
                  return "const $schema: string = \"copeland://interop/transport/v1\"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({\"correlation\":\"" + esc(correlation) + "\",\"kind\":\"" + esc(kind) + "\",\"operation\":\"" + esc(operation) + "\",\"payload\":\"" + esc(payload) + "\",});";
                }
                const lines = require("node:readline").createInterface({ input: process.stdin });
                lines.on("line", line => {
                  if (line.includes("\"kind\":\"handshake\"")) {
                    const protocol = /\"operation\":\"([^\"]*)\"/.exec(line)[1];
                    const digest = /\"payload\":\"([^\"]*)\"/.exec(line)[1];
                    process.stdout.write(envelope("", "handshake", protocol, mode === "mismatch" ? "wrong" : digest) + "\n");
                    return;
                  }
                  const correlation = /\"correlation\":\"([^\"]+)\"/.exec(line)[1];
                  const operation = /\"operation\":\"([^\"]+)\"/.exec(line)[1];
                  if (operation === "lose") { process.exit(23); return; }
                  if (operation === "malformed") { process.stdout.write("not canonical\n"); return; }
                  if (operation === "cancel") { process.stdout.write(envelope(correlation, "cancel", "", "") + "\n"); return; }
                  const frame = operation === "remote" ? envelope(correlation, "remote-error", "", remote) : envelope(correlation, "ok", "", success);
                  setTimeout(() => { process.stdout.write(frame + "\n"); if (operation === "double") process.stdout.write(frame + "\n"); }, correlation === "1" ? 80 : 5);
                });
                """;
        }

        private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
    }

    private const string Source = """
        const $schema: string = "copeland://sidecar/test";
        record Request { value: number; }
        record Response { value: number; label: string; }
        record RemoteError { message: string; }
        function makeRequest(value: number): Request { return { value }; }
        async function call(operation: string, value: number): Response ! RemoteError {
            const request: Request = makeRequest(value);
            const pending: Async<Response ! RemoteError> = tsonCall<Response, RemoteError>(operation, request);
            return await pending;
        }
        """;
}
