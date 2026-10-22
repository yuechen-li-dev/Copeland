using Aurelian.Games;
using TinyFarm;
using TinyFarm.Native;

int bakeOption = Array.IndexOf(args, "--bake-terrain");
if (bakeOption >= 0)
{
    if (bakeOption + 1 >= args.Length)
    {
        throw new ArgumentException("--bake-terrain requires a ground output directory.");
    }
    TinyFarmTerrainBake.Write(Path.Combine(AppContext.BaseDirectory, "Assets"), Path.GetFullPath(args[bakeOption + 1]));
    return;
}

int evidenceOption = Array.IndexOf(args, "--terrain-evidence");
if (evidenceOption >= 0)
{
    if (evidenceOption + 1 >= args.Length)
    {
        throw new ArgumentException("--terrain-evidence requires the native comparison output directory.");
    }
    TinyFarmTerrainEvidence.Write(Path.GetFullPath(args[evidenceOption + 1]));
    return;
}

TinyFarmTerrainMode terrain = TinyFarmTerrainMode.Multiscale;
int terrainOption = Array.IndexOf(args, "--terrain");
if (terrainOption >= 0)
{
    if (terrainOption + 1 >= args.Length || !Enum.TryParse(args[terrainOption + 1], ignoreCase: true, out terrain)
        || !Enum.IsDefined(terrain))
    {
        throw new ArgumentException("--terrain requires legacy, alpha, detail or multiscale.");
    }
}
GameApp.Run(new TinyFarmApp(terrain: terrain), args);
