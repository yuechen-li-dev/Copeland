using Aurelian.Graphics.Vulkan.Native2D;
using Dominatus.SpriteForge;

namespace Aurelian.GameWorld2D;

public sealed class SpritePlaybackState
{
    private readonly Dictionary<WorldPresentationId, PlaybackEntry> entries = [];

    public SpriteFrameMetadata Resolve(
        WorldSprite sprite,
        SpriteForgeAtlas atlas,
        SpriteForgeResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(resolver);

        if (string.IsNullOrWhiteSpace(sprite.ClipId))
        {
            SpriteForgeResolvedFrame staticFrame = resolver.ResolveStaticSprite(atlas, sprite.SpriteId);
            entries.Remove(sprite.StableId);
            return Convert(staticFrame, atlas);
        }

        SpriteForgeSprite definition = atlas.Sprites.TryGetValue(sprite.SpriteId, out SpriteForgeSprite? found)
            ? found
            : throw new KeyNotFoundException($"Unknown sprite asset member '{sprite.SpriteId}'.");
        SpriteForgeAnimation animation = definition.Animations.TryGetValue(sprite.ClipId, out SpriteForgeAnimation? clip)
            ? clip
            : throw new KeyNotFoundException($"Missing atlas clip '{sprite.ClipId}' for sprite '{sprite.SpriteId}'.");
        IReadOnlyList<SpriteForgeResolvedFrame> frames = resolver.ResolveAnimation(atlas, sprite.SpriteId, sprite.ClipId);
        if (frames.Count == 0)
        {
            throw new InvalidOperationException($"Sprite clip '{sprite.ClipId}' has no frames.");
        }

        bool clipChanged = !entries.TryGetValue(sprite.StableId, out PlaybackEntry? previous)
            || previous.SpriteId != sprite.SpriteId
            || previous.ClipId != sprite.ClipId;
        TimeSpan origin = clipChanged || sprite.Restart ? sprite.Elapsed : previous!.Origin;
        entries[sprite.StableId] = new PlaybackEntry(sprite.SpriteId, sprite.ClipId, origin);
        int index = SampleFrameIndex(sprite.Elapsed - origin, animation.Fps, frames.Count, animation.Loop);
        return Convert(frames[index], atlas);
    }

    public static int SampleFrameIndex(TimeSpan elapsed, float framesPerSecond, int frameCount, bool loop)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be positive.");
        }
        if (!float.IsFinite(framesPerSecond) || framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "FPS must be positive and finite.");
        }

        double seconds = Math.Max(0, elapsed.TotalSeconds);
        long sampled = (long)Math.Floor(seconds * framesPerSecond);
        if (loop)
        {
            return (int)(sampled % frameCount);
        }
        return (int)Math.Min(sampled, frameCount - 1);
    }

    private static SpriteFrameMetadata Convert(SpriteForgeResolvedFrame frame, SpriteForgeAtlas atlas)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || atlas.Width <= 0 || atlas.Height <= 0)
        {
            throw new InvalidOperationException("SpriteForge frame and atlas extents must be positive.");
        }
        double localPivotX = frame.PivotX - frame.X;
        double localPivotY = frame.PivotY - frame.Y;
        if (!double.IsFinite(localPivotX) || !double.IsFinite(localPivotY)
            || localPivotX < 0 || localPivotX > frame.Width
            || localPivotY < 0 || localPivotY > frame.Height)
        {
            throw new InvalidOperationException($"SpriteForge frame '{frame.FrameId ?? frame.FrameIndex.ToString()}' has an invalid pivot.");
        }

        string frameId = frame.FrameId ?? $"{frame.SpriteId}:{frame.AnimationId}:{frame.FrameIndex}";
        return new SpriteFrameMetadata(
            frameId,
            frame.X,
            frame.Y,
            frame.Width,
            frame.Height,
            localPivotX,
            localPivotY,
            frame.OffsetX,
            frame.OffsetY,
            frame.Scale,
            new UvRect(
                (double)frame.X / atlas.Width,
                (double)frame.Y / atlas.Height,
                (double)(frame.X + frame.Width) / atlas.Width,
                (double)(frame.Y + frame.Height) / atlas.Height));
    }

    private sealed record PlaybackEntry(string SpriteId, string ClipId, TimeSpan Origin);
}

public sealed class WorldSpriteProjectionAdapter
{
    public IReadOnlyList<OrderedWorldSprite> Project(
        WorldPresentationSnapshot snapshot,
        Camera2DSnapshot camera,
        World2DUnitScale unitScale,
        Func<SpriteAssetId, Native2DTextureHandle> resolveTexture,
        Func<WorldSprite, SpriteFrameMetadata> resolveFrame)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(unitScale);
        ArgumentNullException.ThrowIfNull(resolveTexture);
        ArgumentNullException.ThrowIfNull(resolveFrame);
        unitScale.Validate();

        return snapshot.Sprites
            .OrderBy(sprite => sprite.Layer)
            .ThenBy(sprite => sprite.FeetY)
            .ThenBy(sprite => sprite.StableId.Value, StringComparer.Ordinal)
            .Select(sprite => ProjectOne(sprite, camera, unitScale, resolveTexture(sprite.AssetId), resolveFrame(sprite)))
            .ToArray();
    }

    public PixelPoint2 WorldToPixel(WorldPoint2 point, Camera2DSnapshot camera, World2DUnitScale unitScale)
    {
        unitScale.Validate();
        World2DUnitScale.ValidatePositiveFinite(camera.Zoom, nameof(camera.Zoom));
        double factor = unitScale.PixelsPerWorldUnit * camera.Zoom;
        return new PixelPoint2(
            camera.Viewport.X + (point.X - camera.Position.X) * factor,
            camera.Viewport.Y + (point.Y - camera.Position.Y) * factor);
    }

    private OrderedWorldSprite ProjectOne(
        WorldSprite sprite,
        Camera2DSnapshot camera,
        World2DUnitScale unitScale,
        Native2DTextureHandle texture,
        SpriteFrameMetadata frame)
    {
        if (!double.IsFinite(sprite.Anchor.X) || !double.IsFinite(sprite.Anchor.Y)
            || !double.IsFinite(sprite.FeetY) || !double.IsFinite(sprite.Scale)
            || sprite.Scale <= 0)
        {
            throw new InvalidOperationException($"World sprite '{sprite.StableId}' has a non-finite or invalid transform.");
        }

        PixelPoint2 anchor = WorldToPixel(sprite.Anchor, camera, unitScale);
        double frameScale = frame.Scale * sprite.Scale * camera.Zoom;
        double x = anchor.X + (frame.OffsetX - frame.PivotX) * frameScale;
        double y = anchor.Y + (frame.OffsetY - frame.PivotY) * frameScale;
        double width = frame.Width * frameScale;
        double height = frame.Height * frameScale;
        if (sprite.SnapToIntegerPixels)
        {
            x = Math.Round(x, MidpointRounding.AwayFromZero);
            y = Math.Round(y, MidpointRounding.AwayFromZero);
            width = Math.Round(width, MidpointRounding.AwayFromZero);
            height = Math.Round(height, MidpointRounding.AwayFromZero);
        }

        var submission = new NativeQuadSubmission(
            new Native2DRect((float)x, (float)y, (float)width, (float)height),
            new Native2DUvRect((float)frame.Uv.U0, (float)frame.Uv.V0, (float)frame.Uv.U1, (float)frame.Uv.V1),
            texture,
            sprite.Tint);
        return new OrderedWorldSprite(sprite, frame, submission);
    }
}
