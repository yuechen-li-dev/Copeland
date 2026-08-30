using System.Globalization;
using Machina.Core.Actions;

namespace Oblivion.Product;

public abstract record OblivionInteraction
{
    public sealed record SelectCard(string PageId, string CardId) : OblivionInteraction;

    public sealed record ToggleCardExpansion(string PageId, string CardId) : OblivionInteraction;

    public sealed record CollapseCard(string PageId, string CardId) : OblivionInteraction;

    public sealed record SetScrollOffset(OblivionScrollTarget Target, double Offset) : OblivionInteraction;

    public sealed record SelectCompactCard(string PageId, string CardId) : OblivionInteraction;

    public sealed record ClearCardSelection(string PageId) : OblivionInteraction;

    public sealed record SetCompactPane(OblivionCompactPane Pane) : OblivionInteraction;

    public sealed record InvokeProductAction(OblivionCardActionInvocation Invocation) : OblivionInteraction;
}

public static class OblivionUiActions
{
    private const string Prefix = "oblivion.ui|";

    public static UiActionId SelectCard(string pageId, string cardId)
    {
        return Encode(new OblivionInteraction.SelectCard(pageId, cardId));
    }

    public static UiActionId ToggleCardExpansion(string pageId, string cardId)
    {
        return Encode(new OblivionInteraction.ToggleCardExpansion(pageId, cardId));
    }

    public static UiActionId CollapseCard(string pageId, string cardId)
    {
        return Encode(new OblivionInteraction.CollapseCard(pageId, cardId));
    }

    public static UiActionId SetScrollOffset(OblivionScrollTarget target, double offset)
    {
        return Encode(new OblivionInteraction.SetScrollOffset(target, offset));
    }

    public static UiActionId SelectCompactCard(string pageId, string cardId)
    {
        return Encode(new OblivionInteraction.SelectCompactCard(pageId, cardId));
    }

    public static UiActionId ClearCardSelection(string pageId)
    {
        return Encode(new OblivionInteraction.ClearCardSelection(pageId));
    }

    public static UiActionId SetCompactPane(OblivionCompactPane pane)
    {
        return Encode(new OblivionInteraction.SetCompactPane(pane));
    }

    public static UiActionId InvokeProductAction(OblivionCardActionInvocation invocation)
    {
        return Encode(new OblivionInteraction.InvokeProductAction(invocation));
    }

    public static UiActionId InvokeProductAction(string pageId, string cardId, string actionId)
    {
        return InvokeProductAction(
            new OblivionCardActionInvocation(
                new OblivionCardId(cardId),
                new OblivionProductActionId(actionId),
                pageId,
                SourcePath: null));
    }

    public static UiActionId Encode(OblivionInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        string value = interaction switch
        {
            OblivionInteraction.SelectCard select =>
                $"{Prefix}select-card|{select.PageId}|{select.CardId}",
            OblivionInteraction.ToggleCardExpansion toggle =>
                $"{Prefix}toggle-expansion|{toggle.PageId}|{toggle.CardId}",
            OblivionInteraction.CollapseCard collapse =>
                $"{Prefix}collapse-card|{collapse.PageId}|{collapse.CardId}",
            OblivionInteraction.SetScrollOffset scroll =>
                $"{Prefix}set-scroll|{scroll.Target.Kind}|{scroll.Target.PageId}|{scroll.Target.CardId ?? string.Empty}|{FormatOffset(scroll.Offset)}",
            OblivionInteraction.SelectCompactCard select =>
                $"{Prefix}select-compact-card|{select.PageId}|{select.CardId}",
            OblivionInteraction.ClearCardSelection clear =>
                $"{Prefix}clear-selection|{clear.PageId}",
            OblivionInteraction.SetCompactPane pane =>
                $"{Prefix}set-compact-pane|{pane.Pane}",
            OblivionInteraction.InvokeProductAction invoke =>
                $"{Prefix}invoke-action|{invoke.Invocation.PageId}|{invoke.Invocation.CardId.Value}|{invoke.Invocation.ActionId.Value}",
            _ => throw new ArgumentOutOfRangeException(nameof(interaction), interaction, "Unknown Oblivion interaction."),
        };

        return new UiActionId(value);
    }

    public static bool TryDecode(UiActionId actionId, out OblivionInteraction? interaction)
    {
        if (!actionId.Value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            interaction = null;
            return false;
        }

        string[] parts = actionId.Value[Prefix.Length..].Split('|', StringSplitOptions.None);
        interaction = parts.Length > 0 ? DecodeParts(parts) : null;
        return interaction is not null;
    }

    private static OblivionInteraction? DecodeParts(string[] parts)
    {
        return parts[0] switch
        {
            "select-card" when parts.Length == 3 =>
                new OblivionInteraction.SelectCard(parts[1], parts[2]),
            "toggle-expansion" when parts.Length == 3 =>
                new OblivionInteraction.ToggleCardExpansion(parts[1], parts[2]),
            "collapse-card" when parts.Length == 3 =>
                new OblivionInteraction.CollapseCard(parts[1], parts[2]),
            "set-scroll" when parts.Length == 5 &&
                Enum.TryParse(parts[1], out OblivionScrollTargetKind kind) &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double offset) =>
                new OblivionInteraction.SetScrollOffset(
                    new OblivionScrollTarget(kind, parts[2], EmptyToNull(parts[3])),
                    offset),
            "select-compact-card" when parts.Length == 3 =>
                new OblivionInteraction.SelectCompactCard(parts[1], parts[2]),
            "clear-selection" when parts.Length == 2 =>
                new OblivionInteraction.ClearCardSelection(parts[1]),
            "set-compact-pane" when parts.Length == 2 &&
                Enum.TryParse(parts[1], out OblivionCompactPane pane) =>
                new OblivionInteraction.SetCompactPane(pane),
            "invoke-action" when parts.Length == 4 =>
                new OblivionInteraction.InvokeProductAction(
                    new OblivionCardActionInvocation(
                        new OblivionCardId(parts[2]),
                        new OblivionProductActionId(parts[3]),
                        parts[1],
                        SourcePath: null)),
            _ => null,
        };
    }

    private static string FormatOffset(double offset)
    {
        return offset.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string? EmptyToNull(string value)
    {
        return value.Length == 0 ? null : value;
    }
}
