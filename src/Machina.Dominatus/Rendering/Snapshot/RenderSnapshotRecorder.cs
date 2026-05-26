using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;

namespace Machina.Dominatus.Rendering.Snapshot;

public sealed class RenderSnapshotRecorder
{
    private List<string>? _activeCommands;
    private int _activeWidth;
    private int _activeHeight;
    private int _clipDepth;

    public List<RenderSnapshot> CompletedSnapshots { get; } = new();

    public RenderSnapshot? LastSnapshot => CompletedSnapshots.Count == 0
        ? null
        : CompletedSnapshots[^1];

    public void Record(BeginFrameCommand command)
    {
        if (command.Width <= 0)
        {
            throw new InvalidOperationException("BeginFrame width must be greater than zero.");
        }

        if (command.Height <= 0)
        {
            throw new InvalidOperationException("BeginFrame height must be greater than zero.");
        }

        if (_activeCommands is not null)
        {
            throw new InvalidOperationException("Cannot begin a frame while another frame is active.");
        }

        _activeWidth = command.Width;
        _activeHeight = command.Height;
        _clipDepth = 0;
        _activeCommands = new List<string>
        {
            $"beginFrame w={command.Width} h={command.Height}"
        };
    }

    public void Record(EndFrameCommand _)
    {
        EnsureFrameActive();

        if (_clipDepth != 0)
        {
            throw new InvalidOperationException("Cannot end frame while clip stack is unbalanced.");
        }

        _activeCommands!.Add("endFrame");

        CompletedSnapshots.Add(new RenderSnapshot(_activeWidth, _activeHeight, _activeCommands));

        _activeCommands = null;
        _activeWidth = 0;
        _activeHeight = 0;
    }

    public void Record(FillRectCommand command)
    {
        EnsureFrameActive();
        _activeCommands!.Add($"fillRect id={command.Id} {FormatRect(command.Rect)} color={FormatColor(command.Color)}");
    }

    public void Record(StrokeRectCommand command)
    {
        EnsureFrameActive();
        _activeCommands!.Add(
            $"strokeRect id={command.Id} {FormatRect(command.Rect)} color={FormatColor(command.Color)} thickness={command.Thickness:0.################}");
    }

    public void Record(DrawTextCommand command)
    {
        EnsureFrameActive();
        var color = command.Style.Color is null
            ? "null"
            : FormatColor(command.Style.Color.Value);

        _activeCommands!.Add(
            $"drawText id={command.Id} {FormatRect(command.Rect)} text=\"{RenderSnapshotTextWriter.EscapeText(command.Text)}\" color={color} size={command.Style.Size}");
    }

    public void Record(PushClipCommand command)
    {
        EnsureFrameActive();
        _clipDepth++;
        _activeCommands!.Add($"pushClip id={command.Id} {FormatRect(command.Rect)}");
    }

    public void Record(PopClipCommand _)
    {
        EnsureFrameActive();

        if (_clipDepth <= 0)
        {
            throw new InvalidOperationException("Cannot pop clip when no clip is active.");
        }

        _clipDepth--;
        _activeCommands!.Add("popClip");
    }

    private static string FormatRect(Rect rect) => RenderSnapshotTextWriter.FormatRect(rect);

    private static string FormatColor(ColorToken color) => RenderSnapshotTextWriter.FormatColor(color);

    private void EnsureFrameActive()
    {
        if (_activeCommands is null)
        {
            throw new InvalidOperationException("Render command requires an active frame.");
        }
    }
}
