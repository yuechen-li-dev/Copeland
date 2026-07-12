namespace Aurelian.Core.Presentation.Screens;

public interface IPresenterScreen
{
    ScreenLayerKey Layer { get; }

    bool IsVisible { get; }
}
