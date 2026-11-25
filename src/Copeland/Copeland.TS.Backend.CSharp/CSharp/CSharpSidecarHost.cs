using System.Diagnostics;
using System.Reflection;
using Copeland.TS.Sidecar;

namespace Copeland.TS.Backend.CSharp;

/// <summary>
/// Private CLR host boundary for the M1 sidecar protocol. Generated Copeland
/// code provides canonical envelopes and completion semantics; this class only
/// supplies deployment and byte-stream ownership.
/// </summary>
public interface ICSharpSidecarHostOverride
{
    ValueTask SendAsync(CSharpSidecarContract contract, string canonicalEnvelope, Func<string, ValueTask> receive, CancellationToken cancellationToken);
}

public sealed class CSharpSidecarHost : IAsyncDisposable
{
    private readonly CSharpSidecarContract _contract;
    private readonly ICSharpSidecarHostOverride? _override;
    private SidecarProcessHost? _processHost;

    public CSharpSidecarHost(CSharpSidecarContract contract, ICSharpSidecarHostOverride? hostOverride = null)
    {
        _contract = contract;
        _override = hostOverride;
    }

    public static CSharpSidecarHost Attach(Assembly generatedAssembly, CSharpSidecarContract contract, ICSharpSidecarHostOverride? hostOverride = null)
    {
        var host = new CSharpSidecarHost(contract, hostOverride);
        host.AttachCore(generatedAssembly);
        return host;
    }

    /// <summary>
    /// Attaches the bounded Preview 1 Node sidecar used by a normal SDK-style
    /// project. npm still owns installation; Copeland only launches the
    /// project-authored protocol adapter without requiring a TSPack manifest.
    /// </summary>
    public static CSharpSidecarHost AttachNode(
        Assembly generatedAssembly,
        string projectRoot,
        string scriptPath)
        => Attach(generatedAssembly, NodeContract(projectRoot, scriptPath, string.Empty, string.Empty));

    private static CSharpSidecarContract NodeContract(string projectRoot, string scriptPath, string bindingId, string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        var runTarget = new Manifest.ManifestRunTarget(
            "copeland-preview",
            "npm",
            "node",
            [scriptPath],
            null,
            new Manifest.ManifestValue.Object(
                new Dictionary<string, Manifest.ManifestValue>()));
        return new CSharpSidecarContract(
            bindingId,
            CSharpSidecarContracts.ProtocolVersion,
            digest,
            runTarget,
            Path.GetFullPath(projectRoot));
    }

    private void AttachCore(Assembly generatedAssembly)
    {
        Type[] transports = generatedAssembly.GetTypes()
            .Where(type => string.Equals(type.Name, "CopeTsonTransport", StringComparison.Ordinal))
            .ToArray();
        if (transports.Length != 1)
        {
            throw new InvalidOperationException(
                $"Generated assembly must contain exactly one Copeland transport; found {transports.Length}.");
        }

        Type transport = transports[0];
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        VerifyMetadata(transport, flags);

        FieldInfo dispatch = transport.GetField("Dispatch", flags)
            ?? throw new InvalidOperationException("Generated transport has no host dispatch seam.");
        MethodInfo receive = transport.GetMethod("Receive", flags)
            ?? throw new InvalidOperationException("Generated transport has no receive seam.");
        MethodInfo connectionLost = transport.GetMethod("ConnectionLost", flags)
            ?? throw new InvalidOperationException("Generated transport has no connection-loss seam.");
        MethodInfo envelope = transport.GetMethod("Envelope", flags)
            ?? throw new InvalidOperationException("Generated transport has no canonical envelope writer.");

        _processHost = new SidecarProcessHost(
            BuildStartInfo,
            _contract.ProtocolVersion,
            _contract.ExpectedDigest,
            send => dispatch.SetValue(null, send),
            frame => (bool)receive.Invoke(null, [frame])!,
            () => connectionLost.Invoke(null, null),
            (correlation, kind, operation, payload) => (string)envelope.Invoke(null, [correlation, kind, operation, payload])!,
            _override is null ? null : (frame, callback, cancellation) => _override.SendAsync(_contract, frame, callback, cancellation));
    }

    private void VerifyMetadata(Type transport, BindingFlags flags)
    {
        string binding = ReadConstant(transport, "BindingId", flags);
        string protocol = ReadConstant(transport, "ProtocolVersion", flags);
        string digest = ReadConstant(transport, "ExpectedDigest", flags);
        if (!string.Equals(binding, _contract.LogicalBindingId, StringComparison.Ordinal)
            || !string.Equals(protocol, _contract.ProtocolVersion, StringComparison.Ordinal)
            || !string.Equals(digest, _contract.ExpectedDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Generated sidecar metadata does not match the compiler-owned transport contract.");
        }
    }

    private static string ReadConstant(Type transport, string name, BindingFlags flags)
    {
        FieldInfo field = transport.GetField(name, flags)
            ?? throw new InvalidOperationException($"Generated transport is missing '{name}' metadata.");
        return (string)(field.GetRawConstantValue() ?? string.Empty);
    }

    private ProcessStartInfo BuildStartInfo()
    {
        Manifest.ManifestRunTarget target = _contract.RunTarget;
        string workingDirectory = Path.GetFullPath(_contract.ProjectRoot);
        string runtime = target.Runtime ?? "system";
        ProcessStartInfo startInfo = SidecarProcessHost.CreateStartInfo(string.Empty, workingDirectory);

        if (runtime == "system")
        {
            startInfo.FileName = ResolveExecutable(target.Command[0], workingDirectory);
            foreach (string argument in target.Command.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            startInfo.FileName = runtime switch
            {
                "node" => "node",
                "bun" => "bun",
                "deno" => "deno",
                _ => throw new InvalidOperationException($"Unsupported sidecar runtime '{runtime}'."),
            };
            foreach (string argument in target.Command)
            {
                startInfo.ArgumentList.Add(ResolveProgramArgument(argument, workingDirectory));
            }
        }

        return startInfo;
    }

    private static string ResolveExecutable(string executable, string workingDirectory)
        => Path.IsPathFullyQualified(executable) || !executable.Contains(Path.DirectorySeparatorChar)
            ? executable
            : Path.GetFullPath(Path.Combine(workingDirectory, executable));

    private static string ResolveProgramArgument(string argument, string workingDirectory)
        => Path.IsPathFullyQualified(argument) || argument.StartsWith("-", StringComparison.Ordinal) || !argument.Contains(Path.DirectorySeparatorChar)
            ? argument
            : Path.GetFullPath(Path.Combine(workingDirectory, argument));

    public ValueTask DisposeAsync() => _processHost?.DisposeAsync() ?? ValueTask.CompletedTask;
}
