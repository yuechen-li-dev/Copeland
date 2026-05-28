namespace Machina.Standard.Text;

public abstract record MachinaTextSource;

public sealed record PlainTextSource : MachinaTextSource
{
    public PlainTextSource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}

public sealed record MachinaMarkupSource : MachinaTextSource
{
    public MachinaMarkupSource(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}
