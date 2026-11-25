using System.Diagnostics;
using System.Text;

namespace Copeland.TS.Sidecar;

/// <summary>Owns the child process and framed transport for generated and custom guests.</summary>
public sealed class SidecarProcessHost : IAsyncDisposable
{

    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Func<ProcessStartInfo> _createStartInfo;
    private readonly string _protocol;
    private readonly string _digest;
    private readonly Func<string, bool> _receive;
    private readonly Action _connectionLost;
    private readonly Func<string, string, string, string, string> _envelope;
    private readonly Func<string, Func<string, ValueTask>, CancellationToken, ValueTask>? _override;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _startGate = new();
    private Process? _process;
    private Task? _startTask;
    private TaskCompletionSource<bool>? _handshake;
    private int _lost;
    private int _disposed;

    public SidecarProcessHost(
        Func<ProcessStartInfo> createStartInfo, string protocol, string digest,
        Action<Action<string>> bind, Func<string, bool> receive, Action connectionLost,
        Func<string, string, string, string, string> envelope,
        Func<string, Func<string, ValueTask>, CancellationToken, ValueTask>? transportOverride = null)
    {
        _createStartInfo = createStartInfo;
        _protocol = protocol;
        _digest = digest;
        _receive = receive;
        _connectionLost = connectionLost;
        _envelope = envelope;
        _override = transportOverride;
        bind(Dispatch);
    }

    public static SidecarProcessHost AttachNode(
        string scriptPath, string protocol, string digest,
        Action<Action<string>> bind, Func<string, bool> receive, Action connectionLost,
        Func<string, string, string, string, string> envelope)
    {
        var host = new SidecarProcessHost(() =>
        {
            ProcessStartInfo startInfo = CreateStartInfo("node", AppContext.BaseDirectory);
            startInfo.ArgumentList.Add(Path.GetFullPath(scriptPath, AppContext.BaseDirectory));
            return startInfo;
        }, protocol, digest, bind, receive, connectionLost, envelope);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return host;
    }

    public static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory)
        => new(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            StandardInputEncoding = StrictUtf8,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

    private void Dispatch(string canonicalEnvelope)
    {
        if (Volatile.Read(ref _lost) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            throw new InvalidOperationException("Sidecar connection is closed.");
        }
        _ = DispatchAsync(canonicalEnvelope);
    }

    private async Task DispatchAsync(string canonicalEnvelope)
    {
        try
        {
            if (_override is not null)
            {
                await _override(canonicalEnvelope, ReceiveOverrideAsync, _stopping.Token).ConfigureAwait(false);
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
        ProcessStartInfo startInfo = _createStartInfo();
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => ConnectionLost();
        if (!process.Start())
        {
            throw new InvalidOperationException("The configured sidecar process did not start.");
        }

        _process = process;
        _ = DrainStandardErrorAsync(process, _stopping.Token);
        _ = ReadStandardOutputAsync(process, _stopping.Token);

        string handshake = _envelope(string.Empty, "handshake", _protocol, _digest);
        await WriteFrameAsync(handshake, _stopping.Token).ConfigureAwait(false);
        await _handshake.Task.WaitAsync(TimeSpan.FromSeconds(10), _stopping.Token).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(string frame, CancellationToken cancellationToken)
    {
        if (StrictUtf8.GetByteCount(frame) > SidecarWire.MaximumFrameBytes)
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

            if (bytes.Length == SidecarWire.MaximumFrameBytes)
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
        if (!SidecarWire.TryRead(frame, out string correlation, out string kind, out string operation, out string payload))
        {
            ConnectionLost();
            return;
        }

        if (kind == "handshake")
        {
            if (correlation.Length != 0
                || !string.Equals(operation, _protocol, StringComparison.Ordinal)
                || !string.Equals(payload, _digest, StringComparison.Ordinal))
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

        _receive(frame);
    }

    private void ConnectionLost()
    {
        if (Interlocked.Exchange(ref _lost, 1) != 0)
        {
            return;
        }

        _handshake?.TrySetException(new InvalidOperationException("Sidecar connection was lost."));
        _connectionLost();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _stopping.Cancel();
        ConnectionLost();
        Process? process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                }
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
}
