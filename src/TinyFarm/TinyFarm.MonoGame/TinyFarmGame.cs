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
            PreferredBackBufferWidth = ReadIntOption(args, "--width", 2560),
            PreferredBackBufferHeight = ReadIntOption(args, "--height", 1440),
            SynchronizeWithVerticalRetrace = true
        };
        Window.Title = "TinyFarm M4 - Scenes";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        definitions = TinyFarmDefinitionLoader.Load();
        session = new TinyFarmSession(TinyFarmContent.CreateSceneState(definitions), definitions);
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
        else if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.E))
        {
            ApplyControl(TinyFarmControl.Interact);
        }
        else if (ReadControl(keyboard) is TinyFarmControl control)
        {
            ApplyControl(control);
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
        if (frame.ActiveScene is not null)
        {
            DrawScene(frame);
            return;
        }

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

    private void DrawScene(TinyFarmFrame frame)
    {
        int viewportWidth = GraphicsDevice.Viewport.Width;
        int viewportHeight = GraphicsDevice.Viewport.Height;
        int hudHeight = Math.Clamp(viewportHeight / 12, 76, 112);
        int worldHeight = viewportHeight - hudHeight;
        int tileSize = Math.Max(
            12,
            Math.Min((viewportWidth - 48) / frame.SceneWidth, (worldHeight - 48) / frame.SceneHeight));
        int scenePixelWidth = frame.SceneWidth * tileSize;
        int scenePixelHeight = frame.SceneHeight * tileSize;
        int offsetX = (viewportWidth - scenePixelWidth) / 2;
        int offsetY = (worldHeight - scenePixelHeight) / 2;

        Fill(new Rectangle(offsetX, offsetY, scenePixelWidth, scenePixelHeight), new Color(75, 111, 67));
        for (int x = 0; x <= frame.SceneWidth; x++)
        {
            Fill(new Rectangle(offsetX + (x * tileSize), offsetY, 1, scenePixelHeight), new Color(64, 91, 59));
        }
        for (int y = 0; y <= frame.SceneHeight; y++)
        {
            Fill(new Rectangle(offsetX, offsetY + (y * tileSize), scenePixelWidth, 1), new Color(64, 91, 59));
        }

        foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
        {
            var rectangle = new Rectangle(
                offsetX + (item.Position.X * tileSize),
                offsetY + (item.Position.Y * tileSize),
                item.Width * tileSize,
                item.Height * tileSize);
            Fill(rectangle, SceneObjectColor(item.Kind));
            Border(rectangle, item.BlocksMovement ? new Color(45, 39, 31) : new Color(224, 192, 96), Math.Max(1, tileSize / 24));
            if (item.Kind is SceneObjectKind.Portal or SceneObjectKind.Landmark or SceneObjectKind.Shop)
            {
                int textScale = tileSize >= 72 ? 2 : 1;
                BitmapText.Draw(
                    spriteBatch!,
                    pixel!,
                    item.Label.ToUpperInvariant(),
                    new Vector2(rectangle.X + 4, rectangle.Y + 4),
                    Color.White,
                    textScale);
            }
        }

        foreach (TinyFarmPlotView plot in frame.Plots)
        {
            var rectangle = new Rectangle(
                offsetX + (plot.Position.X * tileSize) + (tileSize / 8),
                offsetY + (plot.Position.Y * tileSize) + (tileSize / 8),
                tileSize * 3 / 4,
                tileSize * 3 / 4);
            Fill(rectangle, plot.WateredToday ? new Color(75, 80, 110) : new Color(112, 73, 46));
            if (plot.Crop is not null)
            {
                int growthHeight = Math.Max(5, tileSize * (plot.GrowthStage + 1) / Math.Max(2, plot.GrowthDays + 1));
                Fill(
                    new Rectangle(rectangle.Center.X - (tileSize / 12), rectangle.Bottom - growthHeight, tileSize / 6, growthHeight),
                    plot.Harvestable ? Color.Gold : new Color(92, 190, 80));
            }
        }

        foreach (TinyFarmActorView actor in frame.Actors)
        {
            int centerX = offsetX + (actor.Position.X * tileSize) + (tileSize / 2);
            int centerY = offsetY + (actor.Position.Y * tileSize) + (tileSize / 2);
            int actorWidth = Math.Max(10, tileSize / 3);
            int actorHeight = Math.Max(16, tileSize / 2);
            var rectangle = new Rectangle(centerX - (actorWidth / 2), centerY - (actorHeight / 2), actorWidth, actorHeight);
            Fill(rectangle, actor.IsPlayer ? new Color(245, 218, 95) : ActorColor(actor.Id));
            Border(rectangle, new Color(30, 30, 30), Math.Max(1, tileSize / 24));
            BitmapText.Draw(
                spriteBatch!,
                pixel!,
                actor.Name.ToUpperInvariant(),
                new Vector2(centerX - (actorWidth / 2), rectangle.Bottom + 2),
                Color.White,
                tileSize >= 72 ? 2 : 1);
        }
    }

    private void DrawHud(TinyFarmFrame frame)
    {
        int width = GraphicsDevice.Viewport.Width;
        int height = GraphicsDevice.Viewport.Height;
        int hudHeight = Math.Clamp(height / 12, 76, 112);
        int top = height - hudHeight;
        int headingScale = height >= 900 ? 2 : 1;
        Fill(new Rectangle(0, top, width, hudHeight), new Color(19, 27, 25));
        BitmapText.Draw(spriteBatch!, pixel!, $"DAY {frame.Day}  {frame.Time}  {frame.CurrentLocationName.ToUpperInvariant()}  {frame.Money}G", new Vector2(18, top + 10), Color.White, headingScale);
        string inventory = frame.Inventory.Count == 0
            ? "INVENTORY EMPTY"
            : "INVENTORY " + string.Join("  ", frame.Inventory.Select(item => $"{item.Name.ToUpperInvariant()} X{item.Count}"));
        BitmapText.Draw(spriteBatch!, pixel!, inventory, new Vector2(18, top + 34), new Color(204, 221, 190), 1);
        string controls = "ARROWS/WASD MOVE  |  ENTER/E INTERACT  |  SPACE WAIT  |  F5 SAVE  |  F9 LOAD";
        string context = string.Join("  |  ", frame.InteractionHints.Skip(4));
        if (frame.Narrative.Count > 0)
        {
            context = context.Length == 0 ? "ENTER CLOSE" : context + "  |  ENTER CLOSE";
        }
        if (context.Length > 0)
        {
            controls += "  |  " + context;
        }
        BitmapText.Draw(spriteBatch!, pixel!, controls.ToUpperInvariant(), new Vector2(18, top + 52), new Color(242, 205, 111), 1);
        string message = frame.Narrative.LastOrDefault() ?? status;
        if (hudHeight >= 96)
        {
            BitmapText.Draw(spriteBatch!, pixel!, message.ToUpperInvariant(), new Vector2(18, top + 72), Color.White, 1);
        }
    }

    private TinyFarmControl? ReadControl(KeyboardState keyboard)
    {
        (Keys Key, TinyFarmControl Control)[] bindings =
        [
            (Keys.Left, TinyFarmControl.MoveLeft),
            (Keys.Right, TinyFarmControl.MoveRight),
            (Keys.Up, TinyFarmControl.MoveUp),
            (Keys.Down, TinyFarmControl.MoveDown),
            (Keys.A, TinyFarmControl.MoveLeft),
            (Keys.D, TinyFarmControl.MoveRight),
            (Keys.W, TinyFarmControl.MoveUp),
            (Keys.S, TinyFarmControl.MoveDown),
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

    private void ApplyControl(TinyFarmControl control)
    {
        GameIntent? intent = TinyFarmHumanController.Map(control, session.State);
        if (intent is null)
        {
            status = "Nothing to do here";
            narrative = [];
            return;
        }

        TinyFarmStepResult step = session.Step(intent);
        narrative = step.Narrative;
        IntentResult human = step.Results.Single(result => result.Envelope.Source == IntentSourceKind.Human);
        status = human.Status == IntentResultStatus.Accepted
            ? intent.GetType().Name.Replace("Intent", string.Empty, StringComparison.Ordinal)
            : $"{human.Status}: {human.Reason}";
    }

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

    private static Color SceneObjectColor(SceneObjectKind kind)
    {
        return kind switch
        {
            SceneObjectKind.Portal => new Color(158, 109, 197),
            SceneObjectKind.Plot => new Color(112, 73, 46),
            SceneObjectKind.Shop => new Color(178, 115, 65),
            SceneObjectKind.Landmark => new Color(190, 164, 105),
            SceneObjectKind.Decoration => new Color(52, 126, 174),
            _ => new Color(91, 103, 70)
        };
    }

    private static string? ReadOption(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ReadIntOption(string[] args, string option, int fallback)
    {
        string? value = ReadOption(args, option);
        return int.TryParse(value, out int parsed) && parsed >= 640 ? parsed : fallback;
    }
}
