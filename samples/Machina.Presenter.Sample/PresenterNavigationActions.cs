using System.Globalization;
using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationActions
{
    public const string SelectSectionPrefix = "presenter.navigation.select-section|";
    public const string SelectTabPrefix = "presenter.navigation.select-tab|";
    public const string SetScrollOffsetPrefix = "presenter.navigation.set-scroll-offset|";
    public const string SelectOblivionCardPrefix = "presenter.navigation.select-oblivion-card|";
    public const string SelectCompactOblivionCardPrefix = "presenter.navigation.select-compact-oblivion-card|";
    public const string ClearOblivionCardSelectionPrefix = "presenter.navigation.clear-oblivion-card-selection|";
    public const string SetCompactPanePrefix = "presenter.navigation.set-compact-pane|";
    public const string InvokeOblivionCardActionPrefix = "presenter.navigation.invoke-oblivion-card-action|";

    public static UiActionId SelectSection(string sectionId)
    {
        return new UiActionId($"{SelectSectionPrefix}{sectionId}");
    }

    public static UiActionId SelectTab(string sectionId, string tabId)
    {
        return new UiActionId($"{SelectTabPrefix}{sectionId}|{tabId}");
    }

    public static UiActionId SetScrollOffset(string pageId, double scrollOffset)
    {
        string offsetText = scrollOffset.ToString("0.###", CultureInfo.InvariantCulture);
        return new UiActionId($"{SetScrollOffsetPrefix}{pageId}|{offsetText}");
    }

    public static UiActionId SelectOblivionCard(string pageId, string cardId)
    {
        return new UiActionId($"{SelectOblivionCardPrefix}{pageId}|{cardId}");
    }

    public static UiActionId SelectCompactOblivionCard(string pageId, string cardId)
    {
        return new UiActionId($"{SelectCompactOblivionCardPrefix}{pageId}|{cardId}");
    }

    public static UiActionId ClearOblivionCardSelection(string pageId)
    {
        return new UiActionId($"{ClearOblivionCardSelectionPrefix}{pageId}");
    }

    public static UiActionId SetCompactPane(PresenterCompactPane compactPane)
    {
        return new UiActionId($"{SetCompactPanePrefix}{compactPane}");
    }

    public static UiActionId InvokeOblivionCardAction(string pageId, string cardId, string actionId)
    {
        return new UiActionId($"{InvokeOblivionCardActionPrefix}{pageId}|{cardId}|{actionId}");
    }

    public static bool TryParseSelectSection(UiActionId actionId, out string sectionId)
    {
        if (actionId.Value.StartsWith(SelectSectionPrefix, StringComparison.Ordinal))
        {
            sectionId = actionId.Value[SelectSectionPrefix.Length..];
            return true;
        }

        sectionId = string.Empty;
        return false;
    }

    public static bool TryParseSelectTab(UiActionId actionId, out string sectionId, out string tabId)
    {
        if (actionId.Value.StartsWith(SelectTabPrefix, StringComparison.Ordinal))
        {
            string payload = actionId.Value[SelectTabPrefix.Length..];
            string[] parts = payload.Split('|', StringSplitOptions.None);
            if (parts.Length == 2)
            {
                sectionId = parts[0];
                tabId = parts[1];
                return true;
            }
        }

        sectionId = string.Empty;
        tabId = string.Empty;
        return false;
    }

    public static bool TryParseSetScrollOffset(UiActionId actionId, out string pageId, out double scrollOffset)
    {
        if (actionId.Value.StartsWith(SetScrollOffsetPrefix, StringComparison.Ordinal))
        {
            string payload = actionId.Value[SetScrollOffsetPrefix.Length..];
            int separator = payload.LastIndexOf('|');
            if (separator > 0)
            {
                pageId = payload[..separator];
                string offsetText = payload[(separator + 1)..];
                if (double.TryParse(offsetText, CultureInfo.InvariantCulture, out scrollOffset))
                {
                    return true;
                }
            }
        }

        pageId = string.Empty;
        scrollOffset = 0;
        return false;
    }

    public static bool TryParseSelectOblivionCard(UiActionId actionId, out string pageId, out string cardId)
    {
        if (actionId.Value.StartsWith(SelectOblivionCardPrefix, StringComparison.Ordinal))
        {
            string payload = actionId.Value[SelectOblivionCardPrefix.Length..];
            string[] parts = payload.Split('|', StringSplitOptions.None);
            if (parts.Length == 2)
            {
                pageId = parts[0];
                cardId = parts[1];
                return true;
            }
        }

        pageId = string.Empty;
        cardId = string.Empty;
        return false;
    }

    public static bool TryParseSelectCompactOblivionCard(UiActionId actionId, out string pageId, out string cardId)
    {
        if (actionId.Value.StartsWith(SelectCompactOblivionCardPrefix, StringComparison.Ordinal))
        {
            string payload = actionId.Value[SelectCompactOblivionCardPrefix.Length..];
            string[] parts = payload.Split('|', StringSplitOptions.None);
            if (parts.Length == 2)
            {
                pageId = parts[0];
                cardId = parts[1];
                return true;
            }
        }

        pageId = string.Empty;
        cardId = string.Empty;
        return false;
    }

    public static bool TryParseClearOblivionCardSelection(UiActionId actionId, out string pageId)
    {
        if (actionId.Value.StartsWith(ClearOblivionCardSelectionPrefix, StringComparison.Ordinal))
        {
            pageId = actionId.Value[ClearOblivionCardSelectionPrefix.Length..];
            return true;
        }

        pageId = string.Empty;
        return false;
    }

    public static bool TryParseSetCompactPane(UiActionId actionId, out PresenterCompactPane compactPane)
    {
        if (actionId.Value.StartsWith(SetCompactPanePrefix, StringComparison.Ordinal))
        {
            string payload = actionId.Value[SetCompactPanePrefix.Length..];
            if (Enum.TryParse(payload, ignoreCase: false, out compactPane))
            {
                return true;
            }
        }

        compactPane = PresenterCompactPane.CardList;
        return false;
    }

    public static bool TryParseInvokeOblivionCardAction(UiActionId actionId, out string pageId, out string cardId, out string actionName)
    {
        if (actionId.Value.StartsWith(InvokeOblivionCardActionPrefix, StringComparison.Ordinal))
        {
            string payload = actionId.Value[InvokeOblivionCardActionPrefix.Length..];
            string[] parts = payload.Split('|', StringSplitOptions.None);
            if (parts.Length == 3)
            {
                pageId = parts[0];
                cardId = parts[1];
                actionName = parts[2];
                return true;
            }
        }

        pageId = string.Empty;
        cardId = string.Empty;
        actionName = string.Empty;
        return false;
    }
}
