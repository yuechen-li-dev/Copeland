using Ariadne.OptFlow.Presentation;
using Aurelian.Ariadne.VnDemo;
using Aurelian.Audio;
using InputMan.Core;
using Xunit;

namespace Sunkill.Tests;

public sealed class SunkillM13Tests
{
    [Fact]
    public void BootRoutingAndQuitUseExplicitApplicationState()
    {
        using var files = new TestFiles();
        using var app = files.CreateApp();

        Assert.Equal(RenScreen.MainMenu, app.State.Screen);
        Assert.Null(app.ActiveGame);

        app.Dispatch(new NewGameIntent());
        Assert.Equal(RenScreen.Game, app.State.Screen);
        Assert.NotNull(app.ActiveGame);

        app.Dispatch(new BackIntent());
        Assert.Equal(RenScreen.PauseMenu, app.State.Screen);
        app.Dispatch(new OpenSettingsIntent());
        Assert.Equal(RenScreen.Settings, app.State.Screen);
        app.Dispatch(new BackIntent());
        Assert.Equal(RenScreen.Game, app.State.Screen);

        app.Dispatch(new OpenSaveMenuIntent());
        Assert.Equal(RenScreen.SaveMenu, app.State.Screen);
        app.Dispatch(new BackIntent());
        app.Dispatch(new OpenLoadMenuIntent());
        Assert.Equal(RenScreen.LoadMenu, app.State.Screen);

        app.Dispatch(new ReturnToMainMenuIntent());
        Assert.Equal(RenScreen.MainMenu, app.State.Screen);
        Assert.Null(app.ActiveGame);

        app.Dispatch(new QuitIntent());
        Assert.True(app.ExitRequested);
    }

    [Fact]
    public void InputManLowersKeyboardNavigationToTypedQuitIntent()
    {
        using var files = new TestFiles();
        using var app = files.CreateApp();

        app.Press(KeyboardKey.ArrowDown);
        app.Press(KeyboardKey.ArrowDown);
        app.Press(KeyboardKey.ArrowDown);
        app.Press(KeyboardKey.Enter);

        Assert.True(app.ExitRequested);
        Assert.Equal("EXIT REQUESTED", app.State.Notice);
    }

    [Theory]
    [InlineData("open-shutters", DawnProtocol.ImmediateShutter, true, false)]
    [InlineData("wait-for-strauss", DawnProtocol.StraussDelay, false, true)]
    public void SunkillChoiceCommitsDistinctSemanticConsequencesAndCompletes(
        string choice,
        DawnProtocol expectedProtocol,
        bool expectedTested,
        bool expectedWaited)
    {
        using var session = new VnSession();
        AdvanceToChoice(session);

        Assert.Equal(
            DialoguePresentationOperationKind.Choice,
            session.Presentation.OperationKind);
        session.Choose(choice);

        Assert.Equal(expectedProtocol, session.Protocol);
        Assert.Equal(expectedTested, session.DawnEngineTested);
        Assert.Equal(expectedWaited, session.StraussWaitedFor);
        Assert.Equal(1, session.ConsequenceEmissionCount);

        while (!session.IsTerminal)
        {
            session.Advance();
        }

        Assert.True(session.IsTerminal);
    }

    [Fact]
    public async Task DeliveranceRestoresLineChoiceAndPostEffectWithoutReplay()
    {
        using var files = new TestFiles();
        var persistence = new VnPersistence(files.SaveDirectory);
        using var session = new VnSession();

        string initialOperation = session.Presentation.OperationId!;
        await persistence.SaveAsync(1, session, DateTimeOffset.UnixEpoch);
        session.Advance();
        await persistence.LoadAsync(1, session);
        Assert.Equal(initialOperation, session.Presentation.OperationId);
        Assert.Equal(0, session.DialogueDispatchCount);

        AdvanceToChoice(session);
        session.MoveChoice(1);
        await persistence.SaveAsync(2, session, DateTimeOffset.UnixEpoch);
        session.Advance();
        await persistence.LoadAsync(2, session);
        Assert.Equal(
            DialoguePresentationOperationKind.Choice,
            session.Presentation.OperationKind);
        Assert.Equal(1, session.Presentation.SelectedChoiceIndex);
        Assert.Equal(0, session.ConsequenceEmissionCount);

        session.MoveChoice(-1);
        session.Advance();
        Assert.Equal(DawnProtocol.ImmediateShutter, session.Protocol);
        Assert.Equal(1, session.ConsequenceEmissionCount);
        string postEffectOperation = session.Presentation.OperationId!;
        await persistence.SaveAsync(3, session, DateTimeOffset.UnixEpoch);
        session.Advance();
        await persistence.LoadAsync(3, session);

        Assert.Equal(postEffectOperation, session.Presentation.OperationId);
        Assert.True(session.DawnEngineTested);
        Assert.Equal(0, session.ConsequenceEmissionCount);
    }

    [Fact]
    public void CorruptLoadIsRejectedWithoutReplacingLiveState()
    {
        using var files = new TestFiles();
        Directory.CreateDirectory(files.SaveDirectory);
        File.WriteAllBytes(
            Path.Combine(files.SaveDirectory, "slot-1.dlv"),
            [0x53, 0x55, 0x4E]);
        using var app = files.CreateApp();

        app.Dispatch(new OpenLoadMenuIntent());
        app.Dispatch(new LoadSlotIntent(1));

        Assert.Equal(RenScreen.LoadMenu, app.State.Screen);
        Assert.Null(app.ActiveGame);
        Assert.Equal("SLOT 1 IS INVALID", app.State.Notice);
    }

    [Fact]
    public void SettingsPersistSeparatelyAndDriveAurelianAudioBuses()
    {
        using var files = new TestFiles();
        RenSettings adjusted;
        using (var app = files.CreateApp())
        {
            app.Dispatch(new OpenSettingsIntent());
            app.Dispatch(new AdjustSettingIntent(-1));
            app.Dispatch(new NavigateIntent(1));
            app.Dispatch(new AdjustSettingIntent(-1));
            app.Dispatch(new NavigateIntent(1));
            for (int index = 0; index < 10; index++)
            {
                app.Dispatch(new AdjustSettingIntent(-1));
            }

            adjusted = app.Settings;
            Assert.Equal(adjusted.MasterVolume, app.AudioFacts.BusGains[AudioBusId.Master]);
            Assert.Equal(adjusted.MusicVolume, app.AudioFacts.BusGains[AudioBusId.Music]);
            Assert.Equal(0f, app.AudioFacts.BusGains[AudioBusId.Sfx]);
        }

        using var restarted = files.CreateApp();
        Assert.Equal(adjusted, restarted.Settings);
        Assert.Equal(adjusted.MasterVolume, restarted.AudioFacts.BusGains[AudioBusId.Master]);
        Assert.True(File.Exists(files.SettingsPath));
        Assert.Empty(Directory.Exists(files.SaveDirectory)
            ? Directory.GetFiles(files.SaveDirectory, "*.dlv")
            : []);
    }

    [Fact]
    public void MalformedSettingsFallBackToValidatedDefaults()
    {
        using var files = new TestFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(files.SettingsPath)!);
        File.WriteAllText(files.SettingsPath, "{ absolutely-not-json }");

        using var app = files.CreateApp();

        Assert.Equal(RenSettings.Default, app.Settings);
    }

    [Fact]
    public void SameSemanticTraceProducesSameFinalHash()
    {
        string first = Replay("open-shutters");
        string second = Replay("open-shutters");

        Assert.Equal(first, second);
    }

    private static string Replay(string choice)
    {
        using var session = new VnSession();
        AdvanceToChoice(session);
        session.Choose(choice);
        while (!session.IsTerminal)
        {
            session.Advance();
        }

        return session.SemanticHash();
    }

    private static void AdvanceToChoice(VnSession session)
    {
        for (int index = 0; index < 16; index++)
        {
            if (session.Presentation.OperationKind == DialoguePresentationOperationKind.Choice)
            {
                return;
            }

            session.Advance();
        }

        throw new InvalidOperationException("SUNKILL did not reach its choice.");
    }

    private sealed class TestFiles : IDisposable
    {
        public TestFiles()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "sunkill-m13-tests",
                Guid.NewGuid().ToString("N"));
            SaveDirectory = Path.Combine(Root, "saves");
            SettingsPath = Path.Combine(Root, "settings", "settings.json");
        }

        public string Root { get; }
        public string SaveDirectory { get; }
        public string SettingsPath { get; }

        public RenApp CreateApp()
        {
            return new RenApp(SaveDirectory, SettingsPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
