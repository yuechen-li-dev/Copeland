using InputMan.Core;

namespace Aurelian.Playtesting;

/// <summary>Game-owned cadence, input routing, semantic commands, and observation.</summary>
public interface IPlaytestTarget<TObservation>
{
    bool Quit { get; }
    void Key(KeyboardKey key, bool down);
    void Pointer(double x, double y, bool? down = null);
    void Text(string text);
    void Scroll(double x, double y, float delta);
    void ReleaseMouse();
    void Frame(TimeSpan elapsed);
    void Focus(bool focused);
    void Resize(int width, int height);
    void Capture(string path);
    (double X, double Y) ActionPoint(string action);
    void SettlePersistence();
    void Command(string command);
    TObservation Observe();

    void MouseDelta(double x, double y) => throw new NotSupportedException("Mouse deltas are not supported by this target.");
    void MouseButton(MouseButton button, bool down) => throw new NotSupportedException("Mouse buttons are not supported by this target.");
}

/// <summary>Exact rewind replays owned inputs into a fresh domain instance; it never patches world facts.</summary>
public interface IReplayablePlaytestTarget
{
    void ResetForReplay();
}
