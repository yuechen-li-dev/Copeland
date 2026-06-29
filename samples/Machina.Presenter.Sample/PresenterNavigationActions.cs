using System.Globalization;
using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationActions
{
    public const string SelectSectionPrefix = "presenter.navigation.select-section|";
    public const string SelectTabPrefix = "presenter.navigation.select-tab|";
    public const string SetScrollOffsetPrefix = "presenter.navigation.set-scroll-offset|";

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
}
