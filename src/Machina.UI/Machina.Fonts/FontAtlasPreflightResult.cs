namespace Machina.Fonts;

public sealed record FontAtlasPreflightResult
{
    public FontAtlasPreflightResult(
        bool success,
        FontAtlasSnapshot snapshot,
        IReadOnlyList<GlyphKey> readyGlyphs,
        IReadOnlyList<GlyphKey> pendingGlyphs,
        IReadOnlyList<GlyphResolution> failures)
    {
        Success = success;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ReadyGlyphs = (readyGlyphs ?? throw new ArgumentNullException(nameof(readyGlyphs))).ToArray();
        PendingGlyphs = (pendingGlyphs ?? throw new ArgumentNullException(nameof(pendingGlyphs))).ToArray();
        Failures = (failures ?? throw new ArgumentNullException(nameof(failures))).ToArray();
    }

    public bool Success { get; }
    public FontAtlasSnapshot Snapshot { get; }
    public IReadOnlyList<GlyphKey> ReadyGlyphs { get; }
    public IReadOnlyList<GlyphKey> PendingGlyphs { get; }
    public IReadOnlyList<GlyphResolution> Failures { get; }
}

public static class FontAtlasPreflight
{
    public static async ValueTask<FontAtlasPreflightResult> EnsureReadyAsync(
        IFontAtlasService service,
        IReadOnlyList<GlyphKey> keys,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(keys);
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        await service.QueueAsync(keys, cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        while (true)
        {
            FontAtlasPreflightResult result = BuildResult(service, keys);
            if (result.PendingGlyphs.Count == 0 || linkedSource.IsCancellationRequested)
            {
                return result;
            }

            try
            {
                if (service is IFontAtlasVersionSource versionSource)
                {
                    long version = versionSource.Version;
                    await versionSource.WaitForVersionChangeAsync(version, linkedSource.Token).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10), linkedSource.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return BuildResult(service, keys);
            }
        }
    }

    private static FontAtlasPreflightResult BuildResult(IFontAtlasService service, IReadOnlyList<GlyphKey> keys)
    {
        List<GlyphKey> ready = [];
        List<GlyphKey> pending = [];
        List<GlyphResolution> failures = [];

        foreach (GlyphKey key in keys.Distinct())
        {
            GlyphResolution resolution = service.Resolve(key);
            if (resolution is GlyphReady)
            {
                ready.Add(key);
            }
            else if (resolution is GlyphMissing)
            {
                failures.Add(resolution);
            }
            else
            {
                pending.Add(key);
            }
        }

        return new FontAtlasPreflightResult(
            pending.Count == 0 && failures.Count == 0,
            service.Snapshot,
            ready,
            pending,
            failures);
    }
}
