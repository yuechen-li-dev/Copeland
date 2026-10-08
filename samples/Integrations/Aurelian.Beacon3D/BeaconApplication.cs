using Aurelian.GameMenus;

namespace Aurelian.Beacon3D;

public enum BeaconScreen
{
    Title,
    Playing,
    Paused,
    Controls,
    Won,
    Lost,
}

/// <summary>Game-owned actions and transitions; the engine supplies menu presentation and navigation.</summary>
public sealed class BeaconApplication
{
    private readonly GameMenuNavigation navigation = new();
    private BeaconScreen returnScreen = BeaconScreen.Title;

    public BeaconGame Game { get; private set; } = new();
    public BeaconScreen Screen { get; private set; } = BeaconScreen.Title;
    public int SelectedIndex => navigation.SelectedIndex;
    public bool ExitRequested { get; private set; }

    public GameMenuPage? Menu => Screen switch
    {
        BeaconScreen.Title => new("beacon.title", "BEACON RUN", "SURVIVE THREE WAVES. COLLECT THREE BEACONS. ESCAPE.",
            [new("new-game", "Start game"), new("controls", "Controls"), new("quit", "Quit")]),
        BeaconScreen.Paused => new("beacon.pause", "PAUSED", "The arena waits while this menu is open.",
            [new("resume", "Resume"), new("restart", "Restart"), new("controls", "Controls"), new("title", "Main menu")]),
        BeaconScreen.Controls => new("beacon.controls", "CONTROLS", "Move around the pillars and collect the gold beacons.",
            [new("movement", "WASD: move   Space: jump", true), new("look", "Mouse: aim   Left click: fire", true),
             new("actions", "R: reload   ESC: pause / release mouse", true), new("back", "Back")]),
        BeaconScreen.Won => new("beacon.win", "YOU WIN!", $"All three beacons collected in {Game.Time:F1} seconds.",
            [new("restart", "Play again"), new("title", "Main menu"), new("quit", "Quit")]),
        BeaconScreen.Lost => new("beacon.lost", "RUN ENDED", $"Wave {Game.Wave} / 3   Stalkers defeated: {Game.Kills}",
            [new("restart", "Try again"), new("title", "Main menu"), new("quit", "Quit")]),
        _ => null,
    };

    public void Update(BeaconCommands commands, float seconds)
    {
        if (ExitRequested)
        {
            return;
        }
        if (Screen == BeaconScreen.Playing)
        {
            if (commands.Pause)
            {
                SetScreen(BeaconScreen.Paused);
                return;
            }
            Game.Step(commands.Movement, seconds);
            if (Game.Dead)
            {
                SetScreen(BeaconScreen.Lost);
            }
            else if (Game.Won)
            {
                SetScreen(BeaconScreen.Won);
            }
            return;
        }
        if (commands.Menu.Back)
        {
            Back();
            return;
        }
        string? action = navigation.Update(Menu!.Entries, commands.Menu);
        if (action is not null)
        {
            Activate(action);
        }
    }

    public void Activate(string action)
    {
        if (Menu is not { } menu || !menu.Entries.Any(entry => entry.Id == action && !entry.Disabled))
        {
            return;
        }
        switch (action)
        {
            case "new-game":
            case "restart":
                Game = new BeaconGame();
                SetScreen(BeaconScreen.Playing);
                break;
            case "resume":
                SetScreen(BeaconScreen.Playing);
                break;
            case "controls":
                returnScreen = Screen;
                SetScreen(BeaconScreen.Controls);
                break;
            case "back":
                Back();
                break;
            case "title":
                SetScreen(BeaconScreen.Title);
                break;
            case "quit":
                ExitRequested = true;
                break;
        }
    }

    private void Back()
    {
        if (Screen == BeaconScreen.Controls)
        {
            SetScreen(returnScreen);
        }
        else if (Screen == BeaconScreen.Paused)
        {
            SetScreen(BeaconScreen.Playing);
        }
        else if (Screen == BeaconScreen.Title)
        {
            ExitRequested = true;
        }
        else if (Screen is BeaconScreen.Won or BeaconScreen.Lost)
        {
            SetScreen(BeaconScreen.Title);
        }
    }

    private void SetScreen(BeaconScreen screen)
    {
        Screen = screen;
        navigation.Reset();
        if (Menu is { } menu)
        {
            navigation.Update(menu.Entries, default);
        }
    }
}
