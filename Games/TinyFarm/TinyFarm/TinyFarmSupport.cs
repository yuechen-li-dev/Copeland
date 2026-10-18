using Aurelian.Audio;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Deliverance.Core.Storage;
using TinyFarm.Runtime;

namespace TinyFarm;

/// <summary>TinyFarm's synthesized placeholder sounds (the shell plays them through its buses).</summary>
internal static class TinyFarmAudio
{
    public static readonly AudioAssetId Confirm = new("tinyfarm.ui.confirm");

    public static void Register(AudioResourceScope resources)
    {
        Add(resources, Confirm, 880, .08);
        Add(resources, TinyFarmAudioAssets.Pickup, 660, .22);
        Add(resources, TinyFarmAudioAssets.Harvest, 440, .24);
        Add(resources, TinyFarmAudioAssets.SwordSwing, 140, .19);
        Add(resources, TinyFarmAudioAssets.Footstep, 90, .05);
        Add(resources, TinyFarmAudioAssets.FarmMusic, 220, 8, music: true);
    }

    private static void Add(AudioResourceScope resources, AudioAssetId id, double frequency, double seconds, bool music = false)
    {
        const int Rate = 48000;
        float[] samples = new float[(int)(Rate * seconds)];
        double[] melody = [1, 1.25, 1.5, 2, 1.5, 1.25, 1.125, 1];
        for (int index = 0; index < samples.Length; index++)
        {
            double time = (double)index / Rate;
            double local = music ? time % 1 : time;
            double envelope = Math.Min(local * 40, 1) * Math.Exp(-local * (music ? 5 : 18));
            double note = frequency * (music ? melody[(int)time % melody.Length] : 1 + time);
            samples[index] = (float)(Math.Sin(time * note * Math.Tau) * envelope * (music ? .12 : .35));
        }
        resources.Add(new AudioClipResource(id, "authored-supper-pcm-v1", Rate, 1, samples.Length, samples));
    }
}

/// <summary>Compiles TinyFarm's own Visual TypeScript shaders shipped beside the game.</summary>
internal static class TinyFarmShaders
{
    public static CompiledGraphicsProgram Compile(string path)
    {
        string source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(Path.GetFileName(path), source)]));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
        {
            throw new InvalidOperationException(backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
        }
        return CompiledGraphicsProgramExporter.Export(module, backend);
    }
}

/// <summary>
/// TinyFarm's own save slots are not used on the shell (the shell stores the semantic snapshot itself). This keeps
/// that code path inert and in memory.
/// </summary>
internal sealed class TinyFarmMemorySaveStore : ISaveStore
{
    private readonly Dictionary<string, byte[]> slots = new(StringComparer.Ordinal);

    public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default) => Task.FromResult(slots.ContainsKey(slotId));

    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(slots.Keys.Order(StringComparer.Ordinal).ToArray());

    public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default) => Task.FromResult<SlotInfo?>(null);

    public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SlotInfo>>([]);

    public Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default) =>
        slots.TryGetValue(slotId, out byte[]? bytes)
            ? Task.FromResult<ReadOnlyMemory<byte>>(bytes)
            : throw new FileNotFoundException($"No in-memory TinyFarm slot '{slotId}'.");

    public Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default)
    {
        slots[slotId] = bytes.ToArray();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string slotId, CancellationToken ct = default)
    {
        slots.Remove(slotId);
        return Task.CompletedTask;
    }
}
