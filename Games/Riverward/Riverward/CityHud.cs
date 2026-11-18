using System.Numerics;
using Aurelian.Games;
using Aurelian.Strategy;

namespace Riverward;

internal readonly record struct HudRect(double Left, double Top, double Width, double Height)
{
    internal bool Contains(Vector2 point) =>
        point.X >= Left && point.X < Left + Width && point.Y >= Top && point.Y < Top + Height;
}

internal static class CityHud
{
    internal const int ButtonWidth = 108;
    private const uint Panel = 0x122823F4;
    private const uint Pale = 0xF2E8CEFF;
    private const uint Muted = 0xBFD1B9FF;
    private const uint Amber = 0xF8CD7BFF;
    private const uint Positive = 0x9EE6ADFF;
    private const uint Negative = 0xFFAC91FF;
    private static readonly Tool[] Tools = Enum.GetValues<Tool>();
    private static readonly uint[] ToolMarks = [0xE7D7ADFF, 0xB7C1BFFF, 0xDF9770FF, 0xD7AD91FF, 0x7FC4D1FF, 0x90BD73FF, 0xE2A08CFF];

    internal static HudRect Toolbar(int width, int height) => new((width - Tools.Length * ButtonWidth) / 2.0, height - 70, Tools.Length * ButtonWidth, 58);
    private static HudRect Summary(int width) => new(12, 12, width - 24, 61);
    private static HudRect Details(int height) => new(12, height - 219, 320, 138);
    private static HudRect Controls(int width, int height) => new(width - 468, height - 100, 456, 25);

    internal static bool BlocksMap(Vector2 point, int width, int height) =>
        Summary(width).Contains(point) || Details(height).Contains(point) || Controls(width, height).Contains(point);

    internal static void Draw(GameHud hud, City city, Tool tool, CityTarget? hover, HexCell? selected, Placement? preview,
        bool paused, bool fast, string notice)
    {
        HudRect summary = Summary(hud.Width);
        AddPanel(hud, summary);
        hud.Text("Riverward", 28, 21, 154, 28, Pale, GameHudTextSize.Medium);
        string time = paused ? "Paused" : fast ? "3x speed" : "Running";
        hud.Text($"Month {city.Month}  /  {time}", 28, 49, 170, 20, Amber);
        hud.Text($"${city.Cash}", 194, 23, 112, 25, Pale, GameHudTextSize.Medium);
        hud.Text($"{Dollars(city.Report.Balance)} / month", 194, 49, 136, 20, city.Report.Balance >= 0 ? Positive : Negative);
        hud.Text($"{city.Report.Residents} housed  /  {city.Report.Housing} capacity", 345, 25, 244, 20, Pale);
        hud.Text($"{city.Report.Employed} employed  /  {city.Report.Jobs} jobs", 345, 49, 244, 20, Muted);
        hud.Text(city.Won ? "Town established" : $"Employ {City.Goal} households", 603, 25, 320, 20, city.Won ? Positive : Pale);
        hud.Text($"Profitable months at target  {city.Streak}/{City.RequiredMonths}", 603, 49, 327, 20, Muted);

        HudRect bar = Toolbar(hud.Width, hud.Height);
        AddPanel(hud, bar);
        foreach (Tool button in Tools)
        {
            int index = (int)button;
            double left = bar.Left + index * ButtonWidth;
            bool active = tool == button;
            hud.Panel(left + 2, bar.Top + 2, ButtonWidth - 4, bar.Height - 4, active ? 0x43674EFFu : 0x233C32FFu);
            hud.Panel(left + 11, bar.Top + 12, 5, 15, ToolMarks[index]);
            hud.Text(button.ToString(), left + 22, bar.Top + 11, ButtonWidth - 25, 20, Pale);
            string price = City.Cost(button) == 0 ? $"{index}" : $"{index}   ${City.Cost(button)}";
            hud.Text(price, left + 22, bar.Top + 34, ButtonWidth - 25, 20, active ? Pale : Muted);
        }

        HudRect details = Details(hud.Height);
        AddPanel(hud, details);
        string[] lines = Describe(city, tool, hover, selected, preview, notice);
        for (int row = 0; row < lines.Length; row++)
        {
            hud.Text(lines[row], details.Left + 12, details.Top + 9 + row * 22,
                details.Width - 22, 22, row == 0 ? Amber : Pale);
        }
        AddPanel(hud, Controls(hud.Width, hud.Height));
        hud.Text("WASD pan  Q/E orbit  Wheel zoom  Space pause  F speed",
            hud.Width - 456, hud.Height - 95, 436, 20, Pale);
    }

    private static string[] Describe(City city, Tool tool, CityTarget? hover, HexCell? selected, Placement? preview, string notice)
    {
        if (preview?.Forecast is { } forecast && hover is { } site)
        {
            string effects = $"Housing {Delta(forecast.Housing - city.Report.Housing)}  Jobs {Delta(forecast.Jobs - city.Report.Jobs)}";
            string balance = $"Budget {Dollars(forecast.Balance - city.Report.Balance)} / month";
            string neighbors = $"{preview.Improved!.Length} blocks gain  /  {preview.Worsened!.Length} lose";
            string impact = neighbors;
            if (tool == Tool.Market && forecast.Blocks.TryGetValue(site.Cell, out BlockReport market))
            {
                impact = $"Serves {market.Customers}/12 nearby households";
            }
            if (tool == Tool.Park)
            {
                impact = $"+2 appeal  /  {preview.Improved!.Length} blocks gain";
            }
            return [tool.ToString(), preview.Message, effects, balance, impact];
        }
        if (preview is not null)
        {
            return [tool.ToString(), preview.Message, notice];
        }
        if (tool == Tool.Inspect && (hover?.Cell ?? selected) is { } cell)
        {
            if (city.Blocks.TryGetValue(cell, out Block block))
            {
                BlockReport details = city.Report.Blocks[cell];
                string access = details.Access ? "Connected to Town Hall" : "Street connection needed";
                return block switch
                {
                    Block.Homes => ["Homes", access, $"{details.Residents} households / {details.Capacity} capacity",
                        $"Appeal {Delta(details.Appeal)}  /  {details.Parks} parks, {details.Noise} workshops",
                        "Apartments need +2 appeal."],
                    Block.Workshop => ["Workshop", access, $"{details.Jobs} jobs", "-2 appeal to adjacent homes"],
                    Block.Market => ["Market", access, $"Serves {details.Customers}/12 nearby households", "$5 income per customer"],
                    Block.Park => ["Park", "+2 appeal to adjacent homes", "$8 monthly upkeep"],
                    _ => [],
                };
            }
            if (cell == CityMap.TownHall)
            {
                return ["Town Hall", "Streets connect here.", "The bridge links both banks."];
            }
            return CityMap.IsWater(cell) ? ["River", "Use the bridge to reach both banks."] : ["Empty block", "Choose a building tool."];
        }
        return tool switch
        {
            Tool.Streets => ["Streets", "Click a hex edge to lay a street.", "$1 upkeep per segment."],
            Tool.Clear => ["Clear", "Click a block center or street edge."],
            Tool.Homes => ["Homes", "Place along a connected street.", "Parks let homes grow into apartments."],
            Tool.Workshop => ["Workshop", "Connected workshops supply 14 jobs.", "Keep noise away from homes."],
            Tool.Market => ["Market", "Place beside occupied homes.", "Connect them to Town Hall by street."],
            Tool.Park => ["Park", "Place beside homes for +2 appeal."],
            _ => ["Inspect", "Point at a block to see its details.", "Choose a tool to preview its effect.", "Space starts city time."],
        };
    }

    private static void AddPanel(GameHud hud, HudRect rect) => hud.Panel(rect.Left, rect.Top, rect.Width, rect.Height, Panel);

    private static string Delta(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static string Dollars(int amount) => amount < 0 ? $"-${-amount}" : $"+${amount}";
}
