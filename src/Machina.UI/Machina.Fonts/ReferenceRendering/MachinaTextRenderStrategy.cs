namespace Machina.Fonts.ReferenceRendering;

public enum MachinaTextRenderStrategy
{
    DirectOutlineStatic,
    MsdfScalableExperimental,
}

public static class MachinaTextRenderStrategyCatalog
{
    public static MachinaTextRenderStrategy DefaultStatic => MachinaTextRenderStrategy.DirectOutlineStatic;

    public static MachinaTextRenderStrategy ScalableExperimental => MachinaTextRenderStrategy.MsdfScalableExperimental;

    public static string GetStableName(MachinaTextRenderStrategy strategy)
    {
        return strategy switch
        {
            MachinaTextRenderStrategy.DirectOutlineStatic => nameof(MachinaTextRenderStrategy.DirectOutlineStatic),
            MachinaTextRenderStrategy.MsdfScalableExperimental => nameof(MachinaTextRenderStrategy.MsdfScalableExperimental),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };
    }

    public static bool IsExperimental(MachinaTextRenderStrategy strategy)
    {
        return strategy == MachinaTextRenderStrategy.MsdfScalableExperimental;
    }

    public static bool IsStaticDefault(MachinaTextRenderStrategy strategy)
    {
        return strategy == DefaultStatic;
    }

    public static MachinaTextRenderStrategy ParseStableName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value switch
        {
            nameof(MachinaTextRenderStrategy.DirectOutlineStatic) => MachinaTextRenderStrategy.DirectOutlineStatic,
            nameof(MachinaTextRenderStrategy.MsdfScalableExperimental) => MachinaTextRenderStrategy.MsdfScalableExperimental,
            _ => throw new InvalidOperationException($"Unknown text render strategy '{value}'."),
        };
    }
}
