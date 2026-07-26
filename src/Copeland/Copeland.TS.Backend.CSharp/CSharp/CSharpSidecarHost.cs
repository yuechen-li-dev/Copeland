using System.Diagnostics;
using System.Reflection;
using System.Text;

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
    private const int MaximumFrameBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CSharpSidecarContract _contract;
    private readonly ICSharpSidecarHostOverride? _override;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _startGate = new();
    private Process? _process;
    private Task? _startTask;
    private TaskCompletionSource<bool>? _handshake;
    private Action? _connectionLost;
    private Func<string, bool>? _receive;
    private Func<string, string, string, string, string>? _envelope;
    private int _lost;

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

    private void AttachCore(Assembly generatedAssembly)
    {
        Type transport = generatedAssembly.GetType("Copeland.Generated.CopeTsonTransport", throwOnError: true)!;
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

        _receive = frame => (bool)receive.Invoke(null, [frame])!;
        _connectionLost = () => connectionLost.Invoke(null, null);
        _envelope = (correlation, kind, operation, payload) => (string)envelope.Invoke(null, [correlation, kind, operation, payload])!;
        dispatch.SetValue(null, new Action<string>(Dispatch));
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

    private void Dispatch(string canonicalEnvelope)
    {
        _ = DispatchAsync(canonicalEnvelope);
    }

    private async Task DispatchAsync(string canonicalEnvelope)
    {
        try
        {
            if (_override is not null)
            {
                await _override.SendAsync(_contract, canonicalEnvelope, ReceiveOverrideAsync, _stopping.Token).ConfigureAwait(false);
                return;
            }

            await EnsureStartedAsync().ConfigureAwait(false);
            await WriteFrameAsync(canonicalEnvelope, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            ConnectionLost();
        }
        catch
        {
            ConnectionLost();
        }
    }

    private ValueTask ReceiveOverrideAsync(string frame)
    {
        HandleIncomingFrame(frame);
        return ValueTask.CompletedTask;
    }

    private Task EnsureStartedAsync()
    {
        lock (_startGate)
        {
            _startTask ??= StartAsync();
            return _startTask;
        }
    }

    private async Task StartAsync()
    {
        ProcessStartInfo startInfo = BuildStartInfo();
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) => ConnectionLost();
        if (!process.Start())
        {
            throw new InvalidOperationException("The configured sidecar process did not start.");
        }

        _process = process;
        _handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = DrainStandardErrorAsync(process, _stopping.Token);
        _ = ReadStandardOutputAsync(process, _stopping.Token);

        string handshake = _envelope!(string.Empty, "handshake", _contract.ProtocolVersion, _contract.ExpectedDigest);
        await WriteFrameAsync(handshake, _stopping.Token).ConfigureAwait(false);
        await _handshake.Task.WaitAsync(_stopping.Token).ConfigureAwait(false);
    }

    private ProcessStartInfo BuildStartInfo()
    {
        Manifest.ManifestRunTarget target = _contract.RunTarget;
        string workingDirectory = Path.GetFullPath(_contract.ProjectRoot);
        string runtime = target.Runtime ?? "system";
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

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

    private async Task WriteFrameAsync(string frame, CancellationToken cancellationToken)
    {
        if (StrictUtf8.GetByteCount(frame) > MaximumFrameBytes)
        {
            throw new InvalidOperationException("Sidecar frame exceeds the M1 byte limit.");
        }

        Process process = _process ?? throw new InvalidOperationException("Sidecar process is not available.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadStandardOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? frame = await ReadFrameAsync(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    ConnectionLost();
                    return;
                }

                HandleIncomingFrame(frame);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            ConnectionLost();
        }
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var oneByte = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(oneByte.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Length == 0 ? null : throw new InvalidOperationException("Sidecar stream ended in a partial frame.");
            }

            if (oneByte[0] == (byte)'\n')
            {
                return StrictUtf8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            }

            if (bytes.Length == MaximumFrameBytes)
            {
                throw new InvalidOperationException("Sidecar frame exceeds the M1 byte limit.");
            }

            bytes.WriteByte(oneByte[0]);
        }
    }

    private async Task DrainStandardErrorAsync(Process process, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            while (await process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
            {
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleIncomingFrame(string frame)
    {
        if (!TryReadEnvelope(frame, out string correlation, out string kind, out string operation, out string payload))
        {
            ConnectionLost();
            return;
        }

        if (kind == "handshake")
        {
            if (correlation.Length != 0
                || !string.Equals(operation, _contract.ProtocolVersion, StringComparison.Ordinal)
                || !string.Equals(payload, _contract.ExpectedDigest, StringComparison.Ordinal))
            {
                ConnectionLost();
                return;
            }

            _handshake?.TrySetResult(true);
            return;
        }

        if (kind is not ("ok" or "remote-error" or "cancel" or "failure") || correlation.Length == 0 || operation.Length != 0)
        {
            ConnectionLost();
            return;
        }

        _receive?.Invoke(frame);
    }

    private void ConnectionLost()
    {
        if (Interlocked.Exchange(ref _lost, 1) != 0)
        {
            return;
        }

        _handshake?.TrySetException(new InvalidOperationException("Sidecar connection was lost."));
        _connectionLost?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        Process? process = _process;
        if (process is not null)
        {
            try
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        _writeGate.Dispose();
        _stopping.Dispose();
    }

    private static bool TryReadEnvelope(string value, out string correlation, out string kind, out string operation, out string payload)
    {
        const string prefix = "const $schema: string = \"copeland://interop/transport/v1\"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({";
        correlation = kind = operation = payload = string.Empty;
        int position = 0;
        bool valid = value.StartsWith(prefix, StringComparison.Ordinal)
            && ReadField(value, ref position, prefix, "\"correlation\":", out correlation)
            && ReadField(value, ref position, string.Empty, "\"kind\":", out kind)
            && ReadField(value, ref position, string.Empty, "\"operation\":", out operation)
            && ReadField(value, ref position, string.Empty, "\"payload\":", out payload)
            && position <= value.Length - 3
            && string.CompareOrdinal(value, position, "});", 0, 3) == 0
            && position + 3 == value.Length;
        return valid;
    }

    private static bool ReadField(string value, ref int position, string prefix, string label, out string result)
    {
        result = string.Empty;
        if (prefix.Length > 0)
        {
            position = prefix.Length;
        }

        if (position > value.Length - label.Length || string.CompareOrdinal(value, position, label, 0, label.Length) != 0)
        {
            return false;
        }

        position += label.Length;
        if (position >= value.Length || value[position++] != '"')
        {
            return false;
        }

        var builder = new StringBuilder();
        while (position < value.Length)
        {
            char current = value[position++];
            if (current == '"')
            {
                if (position >= value.Length || value[position++] != ',')
                {
                    return false;
                }

                result = builder.ToString();
                return true;
            }

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (position >= value.Length)
            {
                return false;
            }

            char escape = value[position++];
            if (escape == 'n') builder.Append('\n');
            else if (escape == 'r') builder.Append('\r');
            else if (escape == 't') builder.Append('\t');
            else if (escape == '"' || escape == '\\') builder.Append(escape);
            else return false;
        }

        return false;
    }
}
