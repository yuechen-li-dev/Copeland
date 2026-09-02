using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TinyFarm.Core;

internal sealed class TinyFarmGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly TinyFarmDefinitions definitions;
    private readonly string savePath;
    private TinyFarmSession session;
    private SpriteBatch? spriteBatch;
    private Texture2D? pixel;
    private KeyboardState previousKeyboard;
    private IReadOnlyList<NarrativeLine> narrative = [];
    private string status = "Welcome to TinyFarm";

    public TinyFarmGame(string[] args)
    {
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1000,
            PreferredBackBufferHeight = 650,
            SynchronizeWithVerticalRetrace = true
        };
        Window.Title = "TinyFarm M3";
        IsMouseVisible = true;
        definitions = TinyFarmDefinitionLoader.Load();
        session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        savePath = ReadOption(args, "--save-file")
            ?? Path.Combine(Environment.CurrentDirectory, "tiny-farm.save");
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData([Color.White]);
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        if (Pressed(keyboard, Keys.Escape))
        {
            Exit();
            return;
        }

        if (Pressed(keyboard, Keys.F5))
        {
            Save();
        }
        else if (Pressed(keyboard, Keys.F9))
        {
            Load();
        }
        else if (Pressed(keyboard, Keys.Enter) && narrative.Count > 0)
        {
            narrative = [];
            status = "Conversation closed";
        }
        else if (ReadControl(keyboard) is TinyFarmControl control)
        {
            GameIntent? intent = TinyFarmHumanController.Map(control, session.State);
            if (intent is null)
            {
                status = "Nothing to do here";
                narrative = [];
            }
            else
            {
                TinyFarmStepResult step = session.Step(intent);
                narrative = step.Narrative;
                IntentResult human = step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
                status = human.Status == IntentResultStatus.Accepted
                    ? intent.GetType().Name.Replace("Intent", string.Empty, StringComparison.Ordinal)
                    : $"{human.Status}: {human.Reason}";
            }
        }

        previousKeyboard = keyboard;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(34, 52, 43));
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions, narrative);
        spriteBatch!.Begin(samplerState: SamplerState.PointClamp);
        DrawWorld(frame);
        DrawHud(frame);
        spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawWorld(TinyFarmFrame frame)
    {
        TinyFarmPoint player = frame.Actors.Single(actor => actor.IsPlayer).Position;
        int cameraX = Math.Clamp(player.X - 400, 0, 120);
        int cameraY = Math.Clamp(player.Y - 250, 0, 80);

        foreach (TinyFarmLocationView location in frame.Locations)
        {
            foreach (LocationId exit in location.Exits.Where(exit => string.CompareOrdinal(location.Id.Value, exit.Value) < 0))
            {
                TinyFarmLocationView target = frame.Locations.Single(candidate => candidate.Id == exit);
                DrawLine(ToVector(location.Position, cameraX, cameraY), ToVector(target.Position, cameraX, cameraY), new Color(112, 91, 62), 12);
            }
        }

        foreach (TinyFarmLocationView location in frame.Locations)
        {
            Vector2 position = ToVector(location.Position, cameraX, cameraY);
            Color color = location.Id == TinyFarmIds.Farmhouse
                ? new Color(105, 155, 78)
                : location.Id == TinyFarmIds.GeneralStore
                    ? new Color(180, 119, 67)
                    : location.Id == TinyFarmIds.Riverside
                        ? new Color(60, 137, 176)
                        : new Color(196, 174, 118);
            Fill(new Rectangle((int)position.X - 72, (int)position.Y - 48, 144, 96), color);
            Border(new Rectangle((int)position.X - 72, (int)position.Y - 48, 144, 96), location.IsCurrent ? Color.Gold : new Color(50, 43, 35), location.IsCurrent ? 5 : 2);
            BitmapText.Draw(spriteBatch!, pixel!, location.Name.ToUpperInvariant(), position + new Vector2(-62, -40), Color.White, 2);
        }

        foreach (TinyFarmPlotView plot in frame.Plots)
        {
            Vector2 position = ToVector(plot.Position, cameraX, cameraY);
            Color soil = plot.WateredToday ? new Color(80, 83, 109) : new Color(112, 73, 46);
            Fill(new Rectangle((int)position.X - 22, (int)position.Y - 15, 44, 30), soil);
            if (plot.Crop is not null)
            {
                int height = 6 + (plot.GrowthStage * 6);
                Color crop = plot.Harvestable ? Color.Gold : new Color(92, 190, 80);
                Fill(new Rectangle((int)position.X - 5, (int)position.Y - height, 10, height), crop);
            }
        }

        foreach (TinyFarmItemView item in frame.GroundItems)
        {
            Vector2 position = ToVector(item.Position, cameraX, cameraY);
            Fill(new Rectangle((int)position.X - 5, (int)position.Y - 5, 10, 10), new Color(127, 220, 130));
        }

        foreach (TinyFarmActorView actor in frame.Actors)
        {
            Vector2 position = ToVector(actor.Position, cameraX, cameraY);
            Color color = actor.IsPlayer ? new Color(245, 218, 95) : ActorColor(actor.Id);
            Fill(new Rectangle((int)position.X - 10, (int)position.Y - 16, 20, 30), color);
            Border(new Rectangle((int)position.X - 10, (int)position.Y - 16, 20, 30), new Color(30, 30, 30), 2);
            BitmapText.Draw(spriteBatch!, pixel!, actor.Name.ToUpperInvariant(), position + new Vector2(-18, 18), Color.White, 1);
        }
    }

    private void DrawHud(TinyFarmFrame frame)
    {
        Fill(new Rectangle(0, 510, graphics.PreferredBackBufferWidth, 140), new Color(19, 27, 25));
        BitmapText.Draw(spriteBatch!, pixel!, $"DAY {frame.Day}  {frame.Time}  {frame.CurrentLocationName.ToUpperInvariant()}  {frame.Money}G", new Vector2(18, 526), Color.White, 2);
        string inventory = frame.Inventory.Count == 0
            ? "INVENTORY EMPTY"
            : "INVENTORY " + string.Join("  ", frame.Inventory.Select(item => $"{item.Name.ToUpperInvariant()} X{item.Count}"));
        BitmapText.Draw(spriteBatch!, pixel!, inventory, new Vector2(18, 554), new Color(204, 221, 190), 1);
        string controls = "ARROWS MOVE  |  SPACE WAIT  |  F5 SAVE  |  F9 LOAD";
        string context = string.Join("  |  ", frame.InteractionHints.Skip(4));
        if (frame.Narrative.Count > 0)
        {
            context = context.Length == 0 ? "ENTER CLOSE" : context + "  |  ENTER CLOSE";
        }
        if (context.Length > 0)
        {
            controls += "  |  " + context;
        }
        BitmapText.Draw(spriteBatch!, pixel!, controls.ToUpperInvariant(), new Vector2(18, 575), new Color(242, 205, 111), 1);
        string message = frame.Narrative.LastOrDefault() ?? status;
        BitmapText.Draw(spriteBatch!, pixel!, message.ToUpperInvariant(), new Vector2(18, 605), Color.White, 1);
    }

    private TinyFarmControl? ReadControl(KeyboardState keyboard)
    {
        (Keys Key, TinyFarmControl Control)[] bindings =
        [
            (Keys.Left, TinyFarmControl.MoveLeft),
            (Keys.Right, TinyFarmControl.MoveRight),
            (Keys.Up, TinyFarmControl.MoveUp),
            (Keys.Down, TinyFarmControl.MoveDown),
            (Keys.L, TinyFarmControl.Look),
            (Keys.E, TinyFarmControl.Talk),
            (Keys.T, TinyFarmControl.Take),
            (Keys.G, TinyFarmControl.Give),
            (Keys.B, TinyFarmControl.Buy),
            (Keys.V, TinyFarmControl.Sell),
            (Keys.P, TinyFarmControl.Plant),
            (Keys.R, TinyFarmControl.Water),
            (Keys.H, TinyFarmControl.Harvest),
            (Keys.Space, TinyFarmControl.Wait)
        ];
        foreach ((Keys key, TinyFarmControl control) in bindings)
        {
            if (Pressed(keyboard, key))
            {
                return control;
            }
        }
        return null;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) => keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key);

    private void Save()
    {
        string fullPath = Path.GetFullPath(savePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, session.CaptureWeekSave());
        status = $"Saved {Path.GetFileName(fullPath)}";
    }

    private void Load()
    {
        try
        {
            session = TinyFarmChunkedSaveCodec.Read(File.ReadAllBytes(Path.GetFullPath(savePath)), definitions);
            narrative = [];
            status = "Loaded authoritative state";
        }
        catch (IOException exception)
        {
            status = exception.Message;
        }
    }

    private void Fill(Rectangle rectangle, Color color) => spriteBatch!.Draw(pixel!, rectangle, color);

    private void Border(Rectangle rectangle, Color color, int width)
    {
        Fill(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, width), color);
        Fill(new Rectangle(rectangle.Left, rectangle.Bottom - width, rectangle.Width, width), color);
        Fill(new Rectangle(rectangle.Left, rectangle.Top, width, rectangle.Height), color);
        Fill(new Rectangle(rectangle.Right - width, rectangle.Top, width, rectangle.Height), color);
    }

    private void DrawLine(Vector2 from, Vector2 to, Color color, int width)
    {
        Vector2 delta = to - from;
        spriteBatch!.Draw(pixel!, from, null, color, MathF.Atan2(delta.Y, delta.X), Vector2.Zero, new Vector2(delta.Length(), width), SpriteEffects.None, 0);
    }

    private static Vector2 ToVector(TinyFarmPoint point, int cameraX, int cameraY) => new(point.X - cameraX, point.Y - cameraY);

    private static Color ActorColor(ActorId id) => id == TinyFarmIds.Mara
        ? new Color(201, 100, 126)
        : id == TinyFarmIds.Elias
            ? new Color(105, 154, 209)
            : new Color(188, 129, 205);

    private static string? ReadOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
