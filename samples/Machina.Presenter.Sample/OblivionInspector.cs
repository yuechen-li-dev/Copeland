using Machina.Core.Actions;
using Machina.Layout.Geometry;

namespace Machina.Presenter.Sample;

public sealed record OblivionCardHitTarget(
    string PageId,
    string CardId,
    Rect Bounds);

public sealed record OblivionPageInteractionMap(
    string PageId,
    IReadOnlyList<OblivionCardHitTarget> CardTargets)
{
    public UiAction? HitTest(PresenterInputPoint point, double scrollOffset)
    {
        double contentX = point.X;
        double contentY = point.Y + scrollOffset;

        foreach (OblivionCardHitTarget target in CardTargets)
        {
            if (contentX >= target.Bounds.X &&
                contentY >= target.Bounds.Y &&
                contentX < target.Bounds.X + target.Bounds.Width &&
                contentY < target.Bounds.Y + target.Bounds.Height)
            {
                return PresenterNavigationActions.SelectOblivionCard(PageId, target.CardId).ToAction();
            }
        }

        return null;
    }
}

public sealed record OblivionInspectorSelection(
    IReadOnlyList<OblivionCard> Cards,
    OblivionCard? SelectedCard,
    string? SelectedCardId);
