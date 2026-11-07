using System.Text.Json;
using Dominatus.Core.Blackboard;
using Marionette.Skyrim;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class SkyrimEmbodimentTests
{
    [Fact]
    public void CanonicalCheckpointPreservesRosterAndSelectionWithoutLiveHandles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "skyrim-embodiment-" + Guid.NewGuid().ToString("N"));
        try
        {
            var owner = new SkyrimWorldOwnerRuntime("old-process");
            owner.RecordEmbodiment(Body(0x020D0FF6, "Skyrim.esm", 0xD0FF6));
            Guid first = owner.ReadEmbodiment()!.SelectedCharacterId!.Value;
            owner.RecordEmbodiment(Body(0x03037057, "Other.esp", 0x37057));
            Guid second = owner.ReadEmbodiment()!.SelectedCharacterId!.Value;
            owner.RecordEmbodiment(Body(0x000D0FF6, "skyrim.esm", 0xD0FF6));
            var selected = owner.ReadEmbodiment()!;
            Assert.Equal(first, selected.SelectedCharacterId);
            Assert.Equal(2, selected.Characters.Length);
            Assert.Contains(selected.Characters, character => character.CharacterId == second);
            string json = JsonSerializer.Serialize(selected, EmbodimentJsonContext.Default.EmbodimentState);
            Assert.DoesNotContain("lease", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("worldGeneration", json);
            Assert.DoesNotContain("targetFormId", json);
            var store = new SkyrimCheckpointStore(directory);
            string bytes = store.ExportEmbedded(owner);
            var restored = new SkyrimCheckpointStore(directory).RestoreEmbedded(bytes, "new-process");
            Assert.Equal(first, restored.ReadEmbodiment()!.SelectedCharacterId);
            Assert.Equal(selected.Characters, restored.ReadEmbodiment()!.Characters);
            owner.ClearEmbodimentSelection();
            Assert.Null(owner.ReadEmbodiment()!.SelectedCharacterId);
            Assert.Equal(2, owner.ReadEmbodiment()!.Characters.Length);
            Assert.Equal(first, restored.ReadEmbodiment()!.SelectedCharacterId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DynamicBodyDoesNotBecomeAPlacedCharacterAndMissingSelectionIsRejected()
    {
        var owner = new SkyrimWorldOwnerRuntime("dynamic");
        owner.RecordEmbodiment(Body(0xD0FF6, "Skyrim.esm", 0xD0FF6));
        owner.RecordEmbodiment(new BodyControlObservation(FormId: 0xFF000800));
        Assert.Null(owner.ReadEmbodiment()!.SelectedCharacterId);
        Assert.Single(owner.ReadEmbodiment()!.Characters);
        Assert.Throws<InvalidDataException>(() => new EmbodimentState(1, Guid.NewGuid(), []).Validate());
        var character = new EmbodiedCharacter(Guid.NewGuid(), "Skyrim.esm", 0xD0FF6);
        Assert.Throws<InvalidDataException>(() => new EmbodimentState(1, character.CharacterId, [character, character]).Validate());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":1,\"selectedCharacterId\":null,\"characters\":[{\"characterId\":\"00000000-0000-0000-0000-000000000001\",\"pluginName\":\"../Skyrim.esm\",\"localFormId\":1}]}")]
    public void MalformedEmbodimentBlocksCheckpointPublication(string payload)
    {
        string directory = Path.Combine(Path.GetTempPath(), "skyrim-embodiment-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = new SkyrimWorldOwnerRuntime("invalid");
            source.DominatusWorld.Bb.Set(new BbKey<string>("skyrim.embodiment.v1"), payload);
            var store = new SkyrimCheckpointStore(directory);
            string bytes = store.ExportEmbedded(source);
            Assert.Throws<InvalidDataException>(() => store.RestoreEmbedded(bytes, "restarted"));
            Assert.Empty(Directory.GetFiles(directory, "*.dom"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ResolvingAPlacedReferenceDoesNotAdoptOrReplaceALiveLease()
    {
        ulong sequence = 0;
        var requests = new List<BodyControlRequest>();
        var controller = new SkyrimBodyControl((request, _) =>
        {
            requests.Add(request);
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                ++sequence, "completed", "", 9, new BodyControlObservation(41, 9, 100, Phase: "active"), 0x020D0FF6));
        });
        await controller.AcquireAsync(new(9, 100));
        SkyrimBodyLease prior = controller.Lease!;
        LoadedActorBody resolved = await controller.ResolveAsync(new SkyrimPlacedActorOrigin("Skyrim.esm", 0xD0FF6), 9);
        Assert.Equal(new LoadedActorBody(9, 0x020D0FF6), resolved);
        Assert.Equal(prior, controller.Lease);
        Assert.Equal("resolve", requests[^1].BodyAction);
        Assert.Equal("skyrim.esm", requests[^1].BodyPluginName);
        Assert.Equal(0xD0FF6U, requests[^1].BodyLocalFormId);
        var unknown = new SkyrimBodyControl((request, _) => ValueTask.FromResult(
            new BodyControlResult(1, "body_control_result", request.RequestId, 1, "completed", "", 9,
                new BodyControlObservation(99, 9, 200), 0xD0FF6)));
        await unknown.ResolveAsync(new SkyrimPlacedActorOrigin("Skyrim.esm", 0xD0FF6), 9);
        Assert.Null(unknown.Lease);
    }

    [Fact]
    public async Task MissingReferenceStaysSelectedWithoutSpawningAndReleaseCancelsRestoration()
    {
        var owner = new SkyrimWorldOwnerRuntime("dormant");
        owner.RecordEmbodiment(Body(0xD0FF6, "Skyrim.esm", 0xD0FF6));
        Guid selected = owner.ReadEmbodiment()!.SelectedCharacterId!.Value;
        var requests = new List<BodyControlRequest>();
        var bodies = new SkyrimBodyControl((request, _) =>
        {
            requests.Add(request);
            return ValueTask.FromResult(new BodyControlResult(1, "body_control_result", request.RequestId,
                (ulong)requests.Count, "failed", "body_reference_unavailable", request.ExpectedWorldGeneration,
                new BodyControlObservation()));
        }, released: owner.ClearEmbodimentSelection);
        var state = new SkyrimStateResult(1, "skyrim_state_result", "state", 1, "completed", null,
            true, 1, true, 0x14, null, false, null, null, false, null, null, 0x14,
            WorldReady: true, WorldPaused: false, WorldGeneration: 3);
        // A world command would fail against Stream.Null: only readonly resolution is allowed here.
        var context = new SkyrimHostContext(Stream.Null, state, owner, bodies);
        var restoration = new SkyrimEmbodimentController();
        await restoration.TickAsync(context, false, CancellationToken.None);
        Assert.Empty(requests);
        await restoration.TickAsync(context, true, CancellationToken.None);
        Assert.Equal("resolve", Assert.Single(requests).BodyAction);
        Assert.Null(bodies.Lease);
        Assert.Equal(selected, owner.ReadEmbodiment()!.SelectedCharacterId);
        await bodies.ReleaseAsync();
        await restoration.TickAsync(context, true, CancellationToken.None);
        Assert.Single(requests);
        Assert.Null(owner.ReadEmbodiment()!.SelectedCharacterId);
        Assert.Single(owner.ReadEmbodiment()!.Characters);
    }

    private static BodyControlObservation Body(uint runtimeForm, string plugin, uint local)
    {
        return new BodyControlObservation(LeaseId: 999, WorldGeneration: 7, FormId: runtimeForm,
            PluginName: plugin, LocalFormId: local);
    }
}
