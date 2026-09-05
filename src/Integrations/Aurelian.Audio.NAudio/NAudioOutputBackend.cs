using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Aurelian.Audio.NAudio;

public sealed class NAudioOutputBackend : IAudioOutputBackend
{
    private readonly BufferedWaveProvider buffer;
    private readonly WaveOutEvent output;
    private bool disposed;

    public NAudioOutputBackend(int sampleRateHz = 48_000, int desiredLatencyMilliseconds = 80)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The M4 NAudio backend uses the Windows WaveOut device path.");
        }
        if (sampleRateHz <= 0 || desiredLatencyMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRateHz, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        output = new WaveOutEvent
        {
            DesiredLatency = desiredLatencyMilliseconds,
            NumberOfBuffers = 3
        };
        output.Init(buffer);
        output.Play();
    }

    public string Name => "NAudio.WaveOut";
    public bool IsAvailable => !disposed && output.PlaybackState != PlaybackState.Stopped;
    public long SubmittedFrames { get; private set; }

    public static bool TryCreate(out NAudioOutputBackend? backend, out string? error)
    {
        try
        {
            backend = new NAudioOutputBackend();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            backend = null;
            error = exception.Message;
            return false;
        }
    }

    public void Submit(ReadOnlySpan<float> interleavedStereoSamples, int sampleRateHz)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sampleRateHz != buffer.WaveFormat.SampleRate)
        {
            throw new ArgumentException($"Backend expects {buffer.WaveFormat.SampleRate} Hz PCM.", nameof(sampleRateHz));
        }
        if (interleavedStereoSamples.Length % 2 != 0)
        {
            throw new ArgumentException("Backend expects complete stereo frames.", nameof(interleavedStereoSamples));
        }
        byte[] bytes = MemoryMarshal.AsBytes(interleavedStereoSamples).ToArray();
        buffer.AddSamples(bytes, 0, bytes.Length);
        SubmittedFrames += interleavedStereoSamples.Length / 2;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        output.Stop();
        output.Dispose();
    }
}
