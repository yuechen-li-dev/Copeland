using Aurelian.Graphics.Vulkan.ComputeMath;
using static Aurelian.Inference.Whisper.TinyEnglishCheckpoint;

namespace Aurelian.Inference.Whisper;

public sealed record WhisperWordTiming(string Text, int FirstSample, int EndSample,
    int UnrefinedFirstSample, int UnrefinedEndSample)
{
    public bool TimingUsable => EndSample > FirstSample;
}

/// <summary>A sequential Tiny.en transcription session. Each call consumes up to 30 seconds of mono 16 kHz PCM.</summary>
public sealed class VulkanWhisperTranscriber : IDisposable, IWhisperQueue
{
    public const int SampleRate = 16000;
    public const int WindowSamples = 30 * SampleRate;
    private const int TokenLimit = TextRows - 2;
    private const int WindowsPerQueue = 32;
    private readonly VulkanInferenceSession session;
    private readonly bool batchAlignment;
    private readonly TinyEnglishCheckpoint checkpoint;
    private readonly TinyEnglishModel model;
    private readonly WhisperLogMel frontend;
    private readonly float[] mel = new float[2 * AudioRows * MelBins];
    private VulkanComputeMath.QueuedComputeScope? retained;
    private int windows;
    private bool disposed;

    VulkanComputeMath.QueuedComputeScope? IWhisperQueue.Retained => retained;

    public VulkanWhisperTranscriber(string checkpointPath) : this(checkpointPath, precompiled: true, batchAlignment: true)
    {
    }

    internal VulkanWhisperTranscriber(string checkpointPath, bool precompiled, bool batchAlignment)
    {
        this.batchAlignment = batchAlignment;
        checkpoint = new TinyEnglishCheckpoint(checkpointPath);
        try
        {
            frontend = new(checkpoint.MelFilters);
            session = VulkanInferenceSession.Create(new()
            {
                ApplicationName = "Aurelian Whisper",
                EnableValidation = false,
                MaximumTensorElements = 32 * 1024 * 1024,
                UseCooperativeLinear = true,
            }, precompiled ? WhisperShaderPack.Load : null);
            try
            {
                model = new(session.Compute, checkpoint, batchAcrossHeads: true, reuseAttention: false,
                    directAttention: true, vectorProjection: true, reuseScratch: true, fusedAttention: true,
                    reuseReadback: true, reuseUpload: true, hostLogits: true, batchCross: true,
                    halfProjections: true, halfAttention: true, joinLogits: false,
                    cooperativeConvolution: true, packedLookup: true)
                {
                    FuseProjectionEpilogues = true, CooperativeAttention = true, AttentionKeyTile = 32,
                    DirectProjectionTiles = true, ReuseProjectionTiles = true,
                };
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch
        {
            checkpoint.Dispose();
            throw;
        }
    }

    public IReadOnlyList<WhisperWordTiming> Transcribe(float[] pcm, int sampleCount, int frontendThreads = 4,
        CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleCount, Math.Min(pcm.Length, WindowSamples));
        ArgumentOutOfRangeException.ThrowIfLessThan(frontendThreads, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frontendThreads, 16);
        cancellation.ThrowIfCancellationRequested();
        foreach (float sample in pcm.AsSpan(0, sampleCount))
        {
            if (!float.IsFinite(sample))
            {
                throw new ArgumentException("Whisper PCM must be finite.", nameof(pcm));
            }
        }
        frontend.Fill(pcm, sampleCount, mel, frontendThreads, cancellation);
        try
        {
            var (tokens, alignment) = model.DecodeAligned(mel, sampleCount, TokenLimit, this, cancellation, batchAlignment);
            return WhisperWordAlignment.Align(checkpoint, tokens, alignment, pcm, sampleCount, cancellation);
        }
        catch
        {
            ResetQueue();
            throw;
        }
    }

    VulkanComputeMath.QueuedComputeScope? IWhisperQueue.OpenRequestScope()
    {
        if (windows == WindowsPerQueue)
        {
            ResetQueue();
        }
        const int batchesPerWindow = 2 + 3 * Layers + 3 * (TokenLimit + 1);
        retained ??= session.Compute.BeginQueuedScope(new TwoSlots(),
            new(WindowsPerQueue * batchesPerWindow, session.Compute.MaximumDispatches, 0));
        windows++;
        return null;
    }

    private void ResetQueue()
    {
        retained?.Dispose();
        retained = null;
        windows = 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            ResetQueue();
        }
        finally
        {
            model.Dispose();
            session.Dispose();
            checkpoint.Dispose();
        }
    }

    private sealed class TwoSlots : IComputeStreamController
    {
        public int Plan(ComputeStreamForecast forecast) => 2;
        public void Observe(ComputeStreamObservation observation) { }
    }
}
