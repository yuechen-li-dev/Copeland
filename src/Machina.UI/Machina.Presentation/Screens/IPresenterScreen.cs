namespace Machina.Presentation.Screens;

/// <summary>
/// Metadata that lets a presenter include a screen in deterministic composition.
/// Screen content remains owned by its producer.
/// </summary>
public interface IPresenterScreen
{
    PresenterScreenId Id { get; }

    ScreenLayerKey Layer { get; }

    bool IsVisible { get; }
}
