using System.Diagnostics;
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
    private const int MaximumWindowsPerRequest = 4;
    private const int MinimumTailSamples = SampleRate / 10;
    private readonly bool resumeTail;
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

    internal VulkanWhisperTranscriber(string checkpointPath, bool precompiled, bool batchAlignment,
        WhisperProfile? startup = null, bool resumeTail = true)
    {
        this.batchAlignment = batchAlignment;
        this.resumeTail = resumeTail;
        long phaseStart = Stopwatch.GetTimestamp();
        checkpoint = new TinyEnglishCheckpoint(checkpointPath);
        WhisperProfile.Record(startup, "checkpoint-index", phaseStart);
        try
        {
            phaseStart = Stopwatch.GetTimestamp();
            frontend = new(checkpoint.MelFilters);
            WhisperProfile.Record(startup, "frontend-setup", phaseStart);
            phaseStart = Stopwatch.GetTimestamp();
            session = VulkanInferenceSession.Create(new()
            {
                ApplicationName = "Aurelian Whisper",
                EnableValidation = false,
                MaximumTensorElements = 32 * 1024 * 1024,
                UseCooperativeLinear = true,
            }, precompiled ? WhisperShaderPack.Load : null,
                startup is null ? null : (name, elapsed) => startup.PhasesMs.Add(name, elapsed));
            WhisperProfile.Record(startup, "vulkan-session", phaseStart);
            try
            {
                phaseStart = Stopwatch.GetTimestamp();
                model = new(session.Compute, checkpoint, batchAcrossHeads: true, reuseAttention: false,
                    directAttention: true, vectorProjection: true, reuseScratch: true, fusedAttention: true,
                    reuseReadback: true, reuseUpload: true, hostLogits: true, batchCross: true,
                    halfProjections: true, halfAttention: true, joinLogits: false,
                    cooperativeConvolution: true, packedLookup: true, startup: startup)
                {
                    FuseProjectionEpilogues = true, CooperativeAttention = true, AttentionKeyTile = 32,
                    DirectProjectionTiles = true, ReuseProjectionTiles = true,
                };
                WhisperProfile.Record(startup, "model-construct", phaseStart);
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
        CancellationToken cancellation = default) =>
        TranscribeProfiled(pcm, sampleCount, frontendThreads, cancellation, profile: null);

    internal IReadOnlyList<WhisperWordTiming> TranscribeProfiled(float[] pcm, int sampleCount, int frontendThreads,
        CancellationToken cancellation, WhisperProfile? profile)
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
        try
        {
            if (profile is not null)
            {
                session.Compute.PipelineCreated = (name, elapsed) => profile.PipelinesMs.Add(name, elapsed);
            }
            var words = new List<WhisperWordTiming>();
            int offset = 0;
            for (int pass = 0; offset < sampleCount; pass++)
            {
                cancellation.ThrowIfCancellationRequested();
                int remaining = sampleCount - offset;
                Func<int, bool>? canResume = null;
                if (resumeTail && pass + 1 < MaximumWindowsPerRequest)
                {
                    canResume = boundary => boundary >= MinimumTailSamples && remaining - boundary >= MinimumTailSamples
                        && HasActiveAudio(pcm.AsSpan(offset + boundary, remaining - boundary));
                }
                long phaseStart = Stopwatch.GetTimestamp();
                frontend.Fill(pcm, offset, remaining, mel, frontendThreads, cancellation);
                WhisperProfile.Record(profile, "pcm-to-mel", phaseStart);
                var (tokens, alignment, covered, next) = model.DecodeAligned(mel, remaining, TokenLimit,
                    this, cancellation, batchAlignment, canResume, profile, offset);
                phaseStart = Stopwatch.GetTimestamp();
                words.AddRange(WhisperWordAlignment.Align(checkpoint, tokens, alignment, pcm, offset, covered, cancellation));
                WhisperProfile.Record(profile, "word-alignment-cpu", phaseStart);
                offset += next;
            }
            return words;
        }
        catch
        {
            ResetQueue();
            throw;
        }
        finally
        {
            session.Compute.PipelineCreated = null;
        }
    }

    private static bool HasActiveAudio(ReadOnlySpan<float> tail)
    {
        const int frame = SampleRate / 50;
        // A single transient near a closed boundary should not start another decode.
        int activeFrames = 0;
        for (int index = 0; index + frame <= tail.Length; index += frame)
        {
            double power = 0;
            foreach (float sample in tail.Slice(index, frame))
            {
                power += (double)sample * sample;
            }
            if (power > frame * 1e-5 && ++activeFrames >= 3)
            {
                return true;
            }
        }
        return false;
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
