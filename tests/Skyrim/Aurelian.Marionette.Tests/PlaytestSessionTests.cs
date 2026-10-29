using System.Text;
using System.IO.Pipes;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class PlaytestSessionTests
{
    [Fact]
    public async Task SessionWaitsForNativeControlsBeforeBeginningInput()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var config = new LocalTransportConfig("playtest-unit-" + Guid.NewGuid().ToString("N"), "test", "test");
        using var server = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var observedKinds = new List<string>();
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await MarionetteWireProtocol.ReadAsync<ClientHello>(server, timeout.Token);
            await MarionetteWireProtocol.WriteAsync(server,
                new ServerHello(1, "server_hello", true, "unit-session", "test", ["playtest_begin"], null), timeout.Token);
            for (ulong sequence = 1; sequence <= 4; sequence++)
            {
                PlaytestRequest request = await MarionetteWireProtocol.ReadAsync<PlaytestRequest>(server, timeout.Token);
                observedKinds.Add(request.MessageKind);
                var result = new PlaytestObservation(1, "playtest_result", request.RequestId, sequence,
                    "completed", "", request.MessageKind == "playtest_begin", false, false, false,
                    0, 0, 0x14, 0, 0, 0, 0, 0) { ControlsReady = sequence > 1 };
                await MarionetteWireProtocol.WriteAsync(server, result, timeout.Token);
            }
        });
        var client = new MarionetteTransportClient(config);
        await using (PlaytestSession session = await client.OpenPlaytestSessionAsync(timeout.Token))
        {
            PlaytestObservation begun = await session.BeginAsync(timeout.Token);
            Assert.True(begun.Active);
        }
        await serve;
        Assert.Equal(["query_playtest_state", "query_playtest_state", "playtest_begin", "playtest_end"], observedKinds);
    }

    [Fact]
    public void PlaytestInputUsesTheExistingBoundedWireEncoding()
    {
        byte[] frame = MarionetteWireProtocol.Encode(new PlaytestRequest(
            1, "playtest_update", "input-1", 2000, (uint)PlaytestButtons.Forward, 300, 12, -8));
        string json = Encoding.UTF8.GetString(frame.AsSpan(4));
        Assert.Contains("\"messageKind\":\"playtest_update\"", json);
        Assert.Contains("\"inputLeaseMilliseconds\":300", json);
        Assert.Contains("\"inputMouseY\":-8", json);
    }

    [Fact]
    public async Task PlaytestObservationsDeserializeThroughGeneratedMetadata()
    {
        var expected = new PlaytestObservation(1, "playtest_result", "sample-1", 23,
            "completed", "", true, false, true, false, 1, 80, 0x14, 10, 20, 30, 1.2f, 0.4f)
        {
            ControlsReady = true,
            ControlFlags = 1023,
            MappedEvents = 79,
            MoveInputY = 1,
        };
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(expected));
        PlaytestObservation actual = await MarionetteWireProtocol.ReadAsync<PlaytestObservation>(stream, default);
        Assert.Equal(expected, actual);
    }
}
