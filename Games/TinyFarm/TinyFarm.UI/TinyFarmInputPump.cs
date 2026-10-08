using InputMan.Aurelian;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

/// <summary>One application input cadence shared by native play and deterministic playtests.</summary>
internal static class TinyFarmInputPump
{
    public static bool Step(TinyFarmGame game, AurelianInputAdapter input, TimeSpan elapsed,
        bool focused, Action processUi)
    {
        string? dialogueBefore = game.Dialogue.Presentation?.OperationId;
        TinyFarmScreen screenBefore = game.Screen;
        int feedbackEpoch = game.FeedbackEpoch;
        processUi();
        game.Handle(input.CurrentFrame);
        bool changed = dialogueBefore != game.Dialogue.Presentation?.OperationId || screenBefore != game.Screen;
        if (feedbackEpoch != game.FeedbackEpoch)
        {
            input.OnFocusChanged(false);
            input.OnFocusChanged(focused);
        }
        else
        {
            game.Advance(elapsed, input.CurrentFrame, focused);
        }
        input.SetContexts(game.Contexts);
        return changed;
    }
}
