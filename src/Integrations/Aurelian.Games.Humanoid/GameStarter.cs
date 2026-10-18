using Aurelian.World.Scenes;

namespace Aurelian.Games;

public static class GameStarter
{
    public static StarterGame Create(string id, IEnumerable<GameConcept> concepts, StarterOptions? options = null,
        string? saveDirectory = null, SceneGroup? sceneDocument = null, HumanoidPlayerOptions? humanoid = null)
    {
        return new(id, new GameDefinition(concepts), options, saveDirectory, sceneDocument: sceneDocument, humanoid: humanoid);
    }

    /// <summary>One explicit bootstrap for native, scripted native and deterministic headless execution.</summary>
    public static void Run(string id, IEnumerable<GameConcept> concepts, string[] args, StarterOptions? options = null,
        Action<StarterGame>? configure = null, SceneGroup? sceneDocument = null, HumanoidPlayerOptions? humanoid = null)
    {
        GameLaunchOptions launch = GameLaunchOptions.Parse(args);
        using var game = new StarterGame(id, new GameDefinition(concepts), options, launch.SaveRoot,
            GameApp.CreateAudioBackend(launch), sceneDocument, humanoid);
        configure?.Invoke(game);
        GameApp.Run(game.Session, launch);
    }
}
