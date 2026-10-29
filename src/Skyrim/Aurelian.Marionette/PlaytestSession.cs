using System.IO.Pipes;

namespace Marionette.Skyrim.App;

[Flags]
public enum PlaytestButtons : uint
{
    None = 0,
    Forward = 1,
    Back = 2,
    Left = 4,
    Right = 8,
    Inventory = 16,
    Activate = 32,
    Jump = 64,
    Pause = 128,
    MenuUp = 256,
    MenuDown = 512,
    MenuLeft = 1024,
    MenuRight = 2048,
    MenuAccept = 4096,
    MenuCancel = 8192,
    MouseLeft = 16384,
}

public sealed record PlaytestRequest(
    int ProtocolVersion, string MessageKind, string RequestId, int TimeoutMilliseconds,
    uint InputButtons = 0, int InputLeaseMilliseconds = 300, float InputMouseX = 0, float InputMouseY = 0);

public sealed record PlaytestObservation(
    int ProtocolVersion,
    string MessageKind,
    string RequestId,
    ulong ServerSequence,
    string Status,
    string FailureReason,
    bool Active,
    bool Foreground,
    bool Moving,
    bool Paused,
    uint Buttons,
    ulong Polls,
    uint PlayerFormId,
    float PositionX,
    float PositionY,
    float PositionZ,
    float Heading,
    float Pitch,
    float CameraW = 0,
    float CameraX = 0,
    float CameraY = 0,
    float CameraZ = 0,
    ulong MousePolls = 0,
    bool ControlsReady = false,
    uint ControlFlags = 0,
    ulong MappedEvents = 0,
    float MoveInputX = 0,
    float MoveInputY = 0,
    string CheckpointName = "",
    uint CellFormId = 0,
    uint CrosshairFormId = 0,
    string CrosshairName = "",
    bool MessageBoxOpen = false,
    bool TravelAutosaveSuppressed = false,
    int MenuAcceptKey = -1,
    ulong MenuEvents = 0,
    float CursorX = 0, float CursorY = 0, float CursorWidth = 0, float CursorHeight = 0);

public sealed partial class MarionetteTransportClient
{
    public async Task<PlaytestSession> OpenPlaytestSessionAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", _config.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            ServerHello hello = await AuthenticateAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (!hello.Capabilities.Contains("playtest_begin", StringComparer.Ordinal))
            {
                throw new InvalidDataException("playtest_capability_missing");
            }
            return new PlaytestSession(pipe);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }
}

/// <summary>One serialized controller connection. Native leases release input after loss of the producer.</summary>
public sealed class PlaytestSession : IAsyncDisposable
{
    private readonly Stream pipe;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ulong lastSequence;
    private bool active;
    private readonly bool ownsPipe;

    internal PlaytestSession(Stream pipe, bool ownsPipe = true)
    {
        this.pipe = pipe;
        this.ownsPipe = ownsPipe;
    }

    public Task<PlaytestObservation> BeginForMenusAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync("playtest_begin", PlaytestButtons.None, 0, 0, cancellationToken);
    }

    public Task<PlaytestObservation> SaveCheckpointAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync("playtest_save", PlaytestButtons.None, 0, 0, cancellationToken);
    }

    public Task<PlaytestObservation> PointMenuAsync(float x, float y, CancellationToken cancellationToken = default)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        return SendAsync("playtest_pointer", PlaytestButtons.None, x, y, cancellationToken);
    }

    public Task<PlaytestObservation> LoadCheckpointAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync("playtest_load", PlaytestButtons.None, 0, 0, cancellationToken);
    }

    public async Task<PlaytestObservation> BeginAsync(CancellationToken cancellationToken = default)
    {
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readiness.CancelAfter(TimeSpan.FromSeconds(10));
        PlaytestObservation? last = null;
        try
        {
            while (!(last = await ObserveAsync(readiness.Token).ConfigureAwait(false)).ControlsReady)
            {
                await Task.Delay(100, readiness.Token).ConfigureAwait(false);
            }
            return await SendAsync("playtest_begin", PlaytestButtons.None, 0, 0, readiness.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (readiness.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"player_controls_not_ready: native control flags={last?.ControlFlags}");
        }
    }

    public Task<PlaytestObservation> ObserveAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync("query_playtest_state", PlaytestButtons.None, 0, 0, cancellationToken);
    }

    public Task<PlaytestObservation> UpdateAsync(PlaytestButtons buttons, float mouseX = 0, float mouseY = 0,
        CancellationToken cancellationToken = default)
    {
        if ((uint)buttons > 32767 || !float.IsFinite(mouseX) || !float.IsFinite(mouseY)
            || Math.Abs(mouseX) > 200 || Math.Abs(mouseY) > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(buttons), "Playtest input exceeds the bounded protocol.");
        }
        return SendAsync("playtest_update", buttons, mouseX, mouseY, cancellationToken);
    }

    public Task<PlaytestObservation> EndAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync("playtest_end", PlaytestButtons.None, 0, 0, cancellationToken);
    }

    /// <summary>Drop the transport without a release command to qualify native controller-loss cleanup.</summary>
    public void AbortConnection()
    {
        active = false;
        pipe.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (active)
            {
                await EndAsync().ConfigureAwait(false);
            }
        }
        catch (IOException) when (!ownsPipe)
        {
            // The owner closes the connection; native disconnect cleanup releases input.
        }
        finally
        {
            if (ownsPipe)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            gate.Dispose();
        }
    }

    private async Task<PlaytestObservation> SendAsync(string kind, PlaytestButtons buttons,
        float mouseX, float mouseY, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            string requestId = Guid.NewGuid().ToString("N");
            var request = new PlaytestRequest(1, kind, requestId, 2000, (uint)buttons, 300, mouseX, mouseY);
            await MarionetteWireProtocol.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
            PlaytestObservation result = await MarionetteWireProtocol.ReadAsync<PlaytestObservation>(pipe, deadline.Token).ConfigureAwait(false);
            if (result.ProtocolVersion != 1 || result.MessageKind != "playtest_result" || result.RequestId != requestId
                || result.ServerSequence <= lastSequence)
            {
                throw new InvalidDataException("playtest_response_correlation_invalid");
            }
            lastSequence = result.ServerSequence;
            if (result.Status != "completed")
            {
                throw new InvalidDataException($"playtest_failed:{result.FailureReason}");
            }
            active = result.Active;
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
