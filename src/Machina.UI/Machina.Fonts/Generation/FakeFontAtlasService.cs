using System.Threading.Channels;

namespace Machina.Fonts.Generation;

public sealed class FakeFontAtlasService : IFontAtlasService, IFontAtlasVersionSource, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Channel<GlyphRequestBatch> channel;
    private readonly FakeGlyphGenerator generator;
    private readonly FakeAtlasPacker packer;
    private readonly CancellationTokenSource disposal = new();
    private readonly TimeSpan processingDelay;
    private readonly Task worker;
    private readonly HashSet<GlyphKey> pending = [];
    private readonly Dictionary<GlyphKey, string> missing = [];
    private TaskCompletionSource<long> versionChanged = CreateVersionChangedSource();
    private FontAtlasSnapshot snapshot = FontAtlasSnapshot.Empty;

    public FakeFontAtlasService(FakeGlyphGenerator? generator = null, FakeAtlasPacker? packer = null, TimeSpan? processingDelay = null)
    {
        this.generator = generator ?? new FakeGlyphGenerator();
        this.packer = packer ?? new FakeAtlasPacker();
        this.processingDelay = processingDelay ?? TimeSpan.Zero;
        channel = Channel.CreateUnbounded<GlyphRequestBatch>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        worker = Task.Run(RunWorkerAsync);
    }

    public FontAtlasSnapshot Snapshot => Volatile.Read(ref snapshot);

    public long Version => Snapshot.Version;

    public GlyphResolution Resolve(GlyphKey key)
    {
        FontAtlasSnapshot current = Snapshot;
        if (current.Glyphs.TryGetValue(key, out GlyphAtlasEntry? entry))
        {
            return new GlyphReady(entry);
        }

        lock (gate)
        {
            if (missing.TryGetValue(key, out string? reason))
            {
                return new GlyphMissing(reason);
            }

            if (pending.Contains(key))
            {
                return new GlyphPending(generator.Generate(key).Metrics);
            }
        }

        return new GlyphPending(null);
    }

    public async ValueTask QueueAsync(IReadOnlyList<GlyphKey> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        List<GlyphKey> accepted = [];

        lock (gate)
        {
            foreach (GlyphKey key in keys.Distinct())
            {
                if (snapshot.Glyphs.ContainsKey(key) || pending.Contains(key) || missing.ContainsKey(key))
                {
                    continue;
                }

                pending.Add(key);
                accepted.Add(key);
            }
        }

        if (accepted.Count > 0)
        {
            await channel.Writer.WriteAsync(new GlyphRequestBatch(accepted), cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask WaitForVersionChangeAsync(long version, CancellationToken cancellationToken = default)
    {
        Task<long> waitTask;
        lock (gate)
        {
            if (snapshot.Version != version)
            {
                return;
            }

            waitTask = versionChanged.Task;
        }

        await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.TryComplete();
        disposal.Cancel();

        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        disposal.Dispose();
    }

    private async Task RunWorkerAsync()
    {
        await foreach (GlyphRequestBatch batch in channel.Reader.ReadAllAsync(disposal.Token).ConfigureAwait(false))
        {
            if (processingDelay > TimeSpan.Zero)
            {
                await Task.Delay(processingDelay, disposal.Token).ConfigureAwait(false);
            }

            ProcessBatch(batch);
        }
    }

    private void ProcessBatch(GlyphRequestBatch batch)
    {
        Dictionary<GlyphKey, GlyphAtlasEntry> additions = [];
        Dictionary<GlyphKey, string> failures = [];

        foreach (GlyphKey key in batch.Keys.OrderBy(static key => key.Face.Value).ThenBy(static key => key.Codepoint).ThenBy(static key => key.EmSize).ThenBy(static key => key.Weight).ThenBy(static key => key.Slant))
        {
            FakeGlyphGenerationResult generated = generator.Generate(key);
            FakeAtlasPackResult packed = packer.Pack(generated);
            if (packed.Entry is not null)
            {
                additions.Add(key, packed.Entry);
            }
            else
            {
                failures.Add(key, packed.MissingReason ?? "Glyph generation failed.");
            }
        }

        lock (gate)
        {
            Dictionary<GlyphKey, GlyphAtlasEntry> nextGlyphs = new(snapshot.Glyphs);
            bool changed = false;

            foreach ((GlyphKey key, GlyphAtlasEntry entry) in additions)
            {
                if (nextGlyphs.TryAdd(key, entry))
                {
                    changed = true;
                }

                pending.Remove(key);
            }

            foreach ((GlyphKey key, string reason) in failures)
            {
                if (!missing.ContainsKey(key))
                {
                    missing.Add(key, reason);
                    changed = true;
                }

                pending.Remove(key);
            }

            if (changed)
            {
                PublishSnapshot(new FontAtlasSnapshot(snapshot.Version + 1, packer.Pages, nextGlyphs));
            }
        }
    }

    private void PublishSnapshot(FontAtlasSnapshot next)
    {
        TaskCompletionSource<long> previous = versionChanged;
        snapshot = next;
        versionChanged = CreateVersionChangedSource();
        previous.TrySetResult(next.Version);
    }

    private static TaskCompletionSource<long> CreateVersionChangedSource()
    {
        return new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
