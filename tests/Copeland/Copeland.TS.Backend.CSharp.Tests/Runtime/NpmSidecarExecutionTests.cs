using System.Reflection;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Manifest;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class NpmSidecarExecutionTests
{
    [Fact]
    public async Task Manifest_resolved_npm_calls_use_canonical_sidecar_tuples_for_values_failures_and_promises()
    {
        string root = Path.Combine(Path.GetTempPath(), "copeland-npm-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sidecar"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "@fixture", "interop"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.tsx"), Manifest, new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@fixture", "interop", "package.json"), "{\"type\":\"module\",\"exports\":\"./index.js\"}", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@fixture", "interop", "index.js"), PackageImplementation, new UTF8Encoding(false));

            ManifestProjectLoadResult manifest = CopelandProject.LoadRootManifest(root);
            Assert.True(manifest.Success, string.Join(Environment.NewLine, manifest.Diagnostics));
            CopelandCompilation source = CopelandCompiler.CompileToMir(Source, new CopelandCompilationOptions
            {
                SourcePath = Path.Combine(root, "main.ts"),
                NpmDependencies = Dependencies,
            });
            Assert.True(source.Success, string.Join(Environment.NewLine, source.Diagnostics));
            CSharpCompilation emitted = CSharpBackend.EmitForRootManifest(source.MirCompilation!.Program!, manifest.Manifest!);
            Assert.Empty(emitted.Diagnostics);
            RoslynCompileResult compiled = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
            AssertZeroArgumentTupleEncodes(compiled.Assembly!);

            ResponsePayloads payloads = ResponsePayloads.Create(compiled.Assembly!);
            AssertRecordResponseDecodes(compiled.Assembly!, payloads.Record);
            await File.WriteAllTextAsync(Path.Combine(root, "sidecar", "fixture.mjs"), Script(payloads), new UTF8Encoding(false));
            await using CSharpSidecarHost host = CSharpSidecarHost.Attach(compiled.Assembly!, emitted.SidecarContract!);

            object zero = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "zero")!;
            await WaitForCompletion(zero);
            Assert.False(IsTransportFailed(zero));

            object sum = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "add", 2d, 3d)!;
            object array = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "mapArray", new[] { 1d, 2d, 3d }, 1d)!;
            object request = CreateInputRecord(compiled.Assembly!, 4d, "record");
            object record = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "mapRecord", request, 5d)!;
            object rejected = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "reject", "bad", 0d)!;
            object slow = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "delayed", 1d, 500d)!;
            object fast = GeneratedModuleInvoker.Invoke(compiled.Assembly!, "delayed", 2d, 5d)!;

            await WaitForCompletion(fast);
            Assert.False(IsTransportFailed(fast));
            Assert.False((bool)slow.GetType().GetProperty("IsCompleted")!.GetValue(slow)!);
            await WaitForCompletion(slow);
            await WaitForCompletion(sum);
            await WaitForCompletion(array);
            await WaitForCompletion(record);
            await WaitForCompletion(rejected);

            Assert.True(ReadOk(zero));
            Assert.True((bool)ReadResultValue(zero));
            Assert.True(ReadOk(sum));
            Assert.Equal(5d, (double)ReadResultValue(sum));
            Assert.True(ReadOk(array));
            Assert.Equal(new[] { 2d, 3d, 4d }, (double[])ReadResultValue(array));
            Assert.True(ReadOk(record));
            AssertRecord(ReadResultValue(record), "record-9", true);

            Assert.False(ReadOk(rejected));
            Assert.False(IsTransportFailed(rejected));
            Assert.Equal("bad", ReadStringProperty(ReadResultError(rejected)));

            Assert.True(ReadOk(slow));
            Assert.True(ReadOk(fast));
            Assert.Equal(new[] { "1" }, (string[])ReadResultValue(slow));
            Assert.Equal(new[] { "2" }, (string[])ReadResultValue(fast));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static readonly CopelandNpmDependencyGraph Dependencies = new(
    [
        new CopelandNpmPackageContract(
            "@fixture/interop",
            "1.0.0",
            [
                new CopelandNpmFunctionContract("zero", [], "boolean", "RemoteError"),
                new CopelandNpmFunctionContract("sum", ["number", "number"], "number", "RemoteError"),
                new CopelandNpmFunctionContract("mirrorArray", ["number[]", "number"], "number[]", "RemoteError"),
                new CopelandNpmFunctionContract("mirrorRecord", ["Input", "number"], "Output", "RemoteError"),
                new CopelandNpmFunctionContract("reject", ["string", "number"], "string", "RejectError"),
                new CopelandNpmFunctionContract("delayed", ["number", "number"], "string[]", "RemoteError", IsPromise: true),
            ]),
    ]);

    private static bool ReadOk(object computation)
        => (bool)ReadResult(computation).GetType().GetProperty("IsOk")!.GetValue(ReadResult(computation))!;

    private static bool IsTransportFailed(object computation)
        => (bool)computation.GetType().GetProperty("IsTransportFailed")!.GetValue(computation)!;

    private static object ReadResult(object computation)
        => computation.GetType().GetProperty("Value")!.GetValue(computation)!;

    private static object ReadResultValue(object computation)
        => ReadResult(computation).GetType().GetProperty("Value")!.GetValue(ReadResult(computation))!;

    private static object ReadResultError(object computation)
        => ReadResult(computation).GetType().GetProperty("Error")!.GetValue(ReadResult(computation))!;

    private static void AssertRecord(object value, string output, bool passed)
    {
        PropertyInfo[] properties = value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(output, properties.Single(property => property.PropertyType == typeof(string)).GetValue(value));
        Assert.Equal(passed, properties.Single(property => property.PropertyType == typeof(bool)).GetValue(value));
    }

    private static string ReadStringProperty(object value)
        => (string)value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Single(property => property.PropertyType == typeof(string)).GetValue(value)!;

    private static object CreateInputRecord(Assembly assembly, double value, string label)
    {
        Type input = assembly.GetTypes().Single(type =>
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
            return type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
                && properties.Length == 2
                && properties[0].PropertyType == typeof(double)
                && properties[1].PropertyType == typeof(string);
        });
        return Activator.CreateInstance(input, BindingFlags.Instance | BindingFlags.NonPublic, null, [value, label], null)!;
    }

    private static void AssertZeroArgumentTupleEncodes(Assembly assembly)
    {
        Type tuple = assembly.GetTypes().Single(type => type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
            && type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Length == 0);
        MethodInfo encoder = assembly.GetType("Copeland.Generated.CopelandModule")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.StartsWith("__tson_encode_", StringComparison.Ordinal)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == tuple);
        object value = Activator.CreateInstance(tuple, BindingFlags.Instance | BindingFlags.NonPublic, null, [], null)!;
        object encoded = encoder.Invoke(null, [value])!;
        Assert.True((bool)encoded.GetType().GetProperty("IsOk")!.GetValue(encoded)!);
    }

    private static void AssertRecordResponseDecodes(Assembly assembly, string payload)
    {
        Type output = assembly.GetTypes().Single(type =>
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
            return type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
                && properties.Select(property => property.PropertyType).SequenceEqual([typeof(string), typeof(bool)]);
        });
        Type wrapper = assembly.GetTypes().Single(type =>
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
            return properties.Length == 1 && properties[0].PropertyType == output;
        });
        MethodInfo decoder = assembly.GetType("Copeland.Generated.CopelandModule")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.StartsWith("__tson_decode_", StringComparison.Ordinal) && method.ReturnType == wrapper);
        Assert.NotNull(decoder.Invoke(null, [payload]));
    }

    private static async Task WaitForCompletion(object computation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(bool)computation.GetType().GetProperty("IsCompleted")!.GetValue(computation)! && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.True((bool)computation.GetType().GetProperty("IsCompleted")!.GetValue(computation)!);
    }

    private sealed record ResponsePayloads(string Zero, string Sum, string Array, string Record, string RemoteError, string DelayedOne, string DelayedTwo)
    {
        public static ResponsePayloads Create(Assembly assembly)
        {
            Type output = FindRecordType(assembly, typeof(string), typeof(bool));
            Type remoteError = FindRecordType(assembly, typeof(string), typeof(double));
            return new ResponsePayloads(
                EncodeWrapper(assembly, typeof(bool), true),
                EncodeWrapper(assembly, typeof(double), 5d),
                EncodeWrapper(assembly, typeof(double[]), new[] { 2d, 3d, 4d }),
                EncodeWrapper(assembly, output, Activator.CreateInstance(output, BindingFlags.Instance | BindingFlags.NonPublic, null, ["record-9", true], null)!),
                EncodeWrapper(assembly, remoteError, Activator.CreateInstance(remoteError, BindingFlags.Instance | BindingFlags.NonPublic, null, ["bad", 13d], null)!),
                EncodeWrapper(assembly, typeof(string[]), new[] { "1" }),
                EncodeWrapper(assembly, typeof(string[]), new[] { "2" }));
        }

        private static Type FindRecordType(Assembly assembly, params Type[] propertyTypes)
        {
            Type[] matches = assembly.GetTypes().Where(type =>
            {
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
                return type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
                    && properties.Select(property => property.PropertyType).SequenceEqual(propertyTypes)
                    && assembly.GetTypes().Any(candidate => candidate.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
                        .Any(property => property.PropertyType == type));
            }).ToArray();
            if (matches.Length == 1) return matches[0];

            string candidates = string.Join("; ", assembly.GetTypes()
                .Where(type => type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal))
                .Select(type => type.Name + "(" + string.Join(",", type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Select(property => property.Name + ":" + property.PropertyType.Name)) + ")"));
            throw new InvalidOperationException("Could not uniquely find nominal record: " + candidates);
        }

        private static string EncodeWrapper(Assembly assembly, Type valueType, object value)
        {
            Type wrapper = assembly.GetTypes().Single(type =>
            {
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic);
                return type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
                    && properties.Length == 1
                    && properties[0].PropertyType == valueType;
            });
            object instance = Activator.CreateInstance(wrapper, BindingFlags.Instance | BindingFlags.NonPublic, null, [value], null)!;
            MethodInfo encoder = assembly.GetType("Copeland.Generated.CopelandModule")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name.StartsWith("__tson_encode_", StringComparison.Ordinal)
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == wrapper);
            object encoded = encoder.Invoke(null, [instance])!;
            return (string)encoded.GetType().GetProperty("Value")!.GetValue(encoded)!;
        }
    }

    private static string Script(ResponsePayloads payloads)
    {
        return $$"""
            import { delayed, mirrorArray, mirrorRecord, reject, sum, zero } from "@fixture/interop";
            const payloads = {
              zero: Buffer.from({{Quote64(payloads.Zero)}}, "base64").toString("utf8"),
              sum: Buffer.from({{Quote64(payloads.Sum)}}, "base64").toString("utf8"),
              array: Buffer.from({{Quote64(payloads.Array)}}, "base64").toString("utf8"),
              record: Buffer.from({{Quote64(payloads.Record)}}, "base64").toString("utf8"),
              remoteError: Buffer.from({{Quote64(payloads.RemoteError)}}, "base64").toString("utf8"),
              delayedOne: Buffer.from({{Quote64(payloads.DelayedOne)}}, "base64").toString("utf8"),
              delayedTwo: Buffer.from({{Quote64(payloads.DelayedTwo)}}, "base64").toString("utf8"),
            };
            function esc(value) { return value.replaceAll("\\", "\\\\").replaceAll("\"", "\\\"").replaceAll("\n", "\\n").replaceAll("\r", "\\r").replaceAll("\t", "\\t"); }
            function envelope(correlation, kind, operation, value) {
              return "const $schema: string = \"copeland://interop/transport/v1\"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({\"correlation\":\"" + esc(correlation) + "\",\"kind\":\"" + esc(kind) + "\",\"operation\":\"" + esc(operation) + "\",\"payload\":\"" + esc(value) + "\",});";
            }
            function send(frame) { process.stdout.write(frame + "\n"); }
            function textPayload(line) {
              const match = /"payload":"((?:\\.|[^"])*)"/.exec(line);
              return JSON.parse("\"" + match[1] + "\"");
            }
            function numberFromBits(bits) {
              const bytes = Buffer.allocUnsafe(8);
              bytes.writeBigUInt64BE(BigInt("0x" + bits));
              return bytes.readDoubleBE();
            }
            function numberField(payload, name) {
              return numberFromBits(new RegExp("\\\"" + name + "\\\":\\s*\\$number\\(\\\"([0-9A-F]+)\\\"\\)").exec(payload)[1]);
            }
            function stringField(payload, name) {
              return JSON.parse("\"" + new RegExp("\\\"" + name + "\\\":\\s*\\\"((?:\\\\.|[^\"])*)\\\"").exec(payload)[1] + "\"");
            }
            function arrayField(payload, name) {
              const body = new RegExp("\\\"" + name + "\\\":\\s*\\[([\\s\\S]*?)\\]").exec(payload)[1];
              return [...body.matchAll(/\$number\("([0-9A-F]+)"\)/g)].map(match => numberFromBits(match[1]));
            }
            const lines = (await import("node:readline")).createInterface({ input: process.stdin });
            lines.on("line", async line => {
              if (line.includes("\"kind\":\"handshake\"")) {
                const protocol = /\"operation\":\"([^\"]*)\"/.exec(line)[1];
                const digest = /\"payload\":\"([^\"]*)\"/.exec(line)[1];
                process.stdout.write(envelope("", "handshake", protocol, digest) + "\n");
                return;
              }
              const correlation = /\"correlation\":\"([^\"]+)\"/.exec(line)[1];
              const operation = /\"operation\":\"([^\"]+)\"/.exec(line)[1];
              try {
                const request = textPayload(line);
                if (operation.endsWith(":zero")) {
                  if (zero() !== true) throw new Error("zero export was not invoked");
                  send(envelope(correlation, "ok", "", payloads.zero));
                } else if (operation.endsWith(":sum")) {
                  if (sum(numberField(request, "arg0"), numberField(request, "arg1")) !== 5) throw new Error("sum arguments were not decoded");
                  send(envelope(correlation, "ok", "", payloads.sum));
                } else if (operation.endsWith(":mirrorArray")) {
                  const values = mirrorArray(arrayField(request, "arg0"), numberField(request, "arg1"));
                  if (values.join(",") !== "2,3,4") throw new Error("array tuple was not decoded");
                  send(envelope(correlation, "ok", "", payloads.array));
                } else if (operation.endsWith(":mirrorRecord")) {
                  const result = mirrorRecord({ value: numberField(request, "value"), label: stringField(request, "label") }, numberField(request, "arg1"));
                  if (result.output !== "record-9" || result.passed !== true) throw new Error("record tuple was not decoded");
                  send(envelope(correlation, "ok", "", payloads.record));
                } else if (operation.endsWith(":reject")) {
                  try {
                    reject(stringField(request, "arg0"), numberField(request, "arg1"));
                    throw new Error("reject export unexpectedly returned");
                  } catch (error) {
                    if (error.message !== "bad") throw error;
                    send(envelope(correlation, "remote-error", "", payloads.remoteError));
                  }
                } else if (operation.endsWith(":delayed")) {
                  const value = numberField(request, "arg0");
                  const result = await delayed(value, numberField(request, "arg1"));
                  if (result[0] !== String(value)) throw new Error("Promise export was not invoked");
                  send(envelope(correlation, "ok", "", value === 1 ? payloads.delayedOne : payloads.delayedTwo));
                } else {
                  throw new Error("unknown npm operation: " + operation);
                }
              } catch (error) {
                send(envelope(correlation, "failure", "", ""));
              }
            });
            """;
    }

    private static string Quote64(string value)
        => System.Text.Json.JsonSerializer.Serialize(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));

    private const string Manifest = """
        import { Package, RunTargets, Sidecars, Workspace, define } from "tspack/manifest";
        export default define(<Workspace name="sample"><Package name="app" version="1" kind="app"><RunTargets rows={[{ name: "node", runtime: "node", cwd: "workspace", command: ["sidecar/fixture.mjs"] }]} /></Package><Sidecars rows={[{ id: "node-transport", runTarget: "sample/app/node", default: true }]} /></Workspace>);
        """;

    private const string PackageImplementation = """
        export function zero() { return true; }
        export function sum(left, right) { return left + right; }
        export function mirrorArray(values, increment) { return values.map(value => value + increment); }
        export function mirrorRecord(request, increment) { return { output: request.label + "-" + (request.value + increment), passed: true }; }
        export function reject(message) { throw new Error(message); }
        export async function delayed(value, delay) { await new Promise(resolve => setTimeout(resolve, delay)); return [String(value)]; }
        """;

    private const string Source = """
        import { delayed as npmDelayed, mirrorArray as npmMirrorArray, mirrorRecord as npmMirrorRecord, reject as npmReject, sum as npmSum, zero as npmZero } from "@fixture/interop";
        const $schema: string = "copeland://npm/test";
        record Input { value: number; label: string; }
        record Output { output: string; passed: boolean; }
        record RemoteError { message: string; }
        record RejectError { message: string; code: number; }
        async function zero(): boolean ! RemoteError { const pending: Async<boolean ! RemoteError> = npmZero(); return await pending; }
        async function add(left: number, right: number): number ! RemoteError { const pending: Async<number ! RemoteError> = npmSum(left, right); return await pending; }
        async function mapArray(values: number[], increment: number): number[] ! RemoteError { const pending: Async<number[] ! RemoteError> = npmMirrorArray(values, increment); return await pending; }
        async function mapRecord(input: Input, increment: number): Output ! RemoteError { const pending: Async<Output ! RemoteError> = npmMirrorRecord(input, increment); return await pending; }
        async function reject(message: string, code: number): string ! RejectError { const pending: Async<string ! RejectError> = npmReject(message, code); return await pending; }
        async function delayed(value: number, delay: number): string[] ! RemoteError { const pending: Async<string[] ! RemoteError> = npmDelayed(value, delay); return await pending; }
        """;
}
