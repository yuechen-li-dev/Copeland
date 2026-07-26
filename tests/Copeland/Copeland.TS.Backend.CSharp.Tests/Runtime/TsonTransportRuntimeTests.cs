using System.Reflection;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class TsonTransportRuntimeTests
{
    [Fact]
    public void Delayed_tson_response_resumes_the_explicit_async_plan_once()
    {
        Assembly assembly = Compile("""
            const $schema: string = "copeland://transport/test";

            record Request { value: number; }
            record Response { value: number; label: string; }
            record RemoteError { message: string; }

            function makeRequest(value: number): Request { return { value }; }

            async function load(value: number): Response ! RemoteError {
                const request: Request = makeRequest(value);
                const pending: Async<Response ! RemoteError> = tsonCall<Response, RemoteError>("double", request);
                return await pending;
            }
            """);

        Type transport = assembly.GetType("Copeland.Generated.CopeTsonTransport")!;
        var requests = new List<string>();
        FieldInfo dispatch = transport.GetField("Dispatch", BindingFlags.Static | BindingFlags.NonPublic)!;
        dispatch.SetValue(null, new Action<string>(requests.Add));

        object computation = GeneratedModuleInvoker.Invoke(assembly, "load", 21.0)!;
        Type computationType = computation.GetType();
        Assert.False((bool)computationType.GetProperty("IsCompleted")!.GetValue(computation)!);
        string request = Assert.Single(requests);
        Assert.Contains("copeland://interop/transport/v1", request, StringComparison.Ordinal);
        Assert.Contains("copeland://transport/test", request, StringComparison.Ordinal);

        string payload = EncodeResponse(assembly, 42.0);
        Assert.Contains("$record.Response", payload, StringComparison.Ordinal);
        MethodInfo envelope = transport.GetMethod("Envelope", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo receive = transport.GetMethod("Receive", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo decoder = assembly.GetType("Copeland.Generated.CopelandModule")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.StartsWith("__tson_decode_", StringComparison.Ordinal)
                && method.ReturnType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Length == 2);
        Assert.NotNull(decoder.Invoke(null, [payload]));
        string response = (string)envelope.Invoke(null, ["1", "ok", string.Empty, payload])!;

        Assert.True((bool)receive.Invoke(null, [response])!);
        Assert.False((bool)receive.Invoke(null, [response])!);
        Assert.True((bool)computationType.GetProperty("IsCompleted")!.GetValue(computation)!);
        object result = computationType.GetProperty("Value")!.GetValue(computation)!;
        Assert.True((bool)result.GetType().GetProperty("IsOk")!.GetValue(result)!);
        object responseValue = result.GetType().GetProperty("Value")!.GetValue(result)!;
        Assert.Equal(42.0, responseValue.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Single(property => property.PropertyType == typeof(double)).GetValue(responseValue));
    }

    private static string EncodeResponse(Assembly assembly, double value)
    {
        Type responseType = assembly.GetTypes().Single(type => type.Name.StartsWith("__CopeRecord_", StringComparison.Ordinal)
            && type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Length == 2);
        object response = Activator.CreateInstance(responseType, BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [value, "ok"], culture: null)!;
        Type module = assembly.GetType("Copeland.Generated.CopelandModule")!;
        MethodInfo encoder = module.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name.StartsWith("__tson_encode_", StringComparison.Ordinal)
                && method.GetParameters().SingleOrDefault()?.ParameterType == responseType);
        object result = encoder.Invoke(null, [response])!;
        return (string)result.GetType().GetProperty("Value")!.GetValue(result)!;
    }

    private static Assembly Compile(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult result = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Assembly!;
    }
}
