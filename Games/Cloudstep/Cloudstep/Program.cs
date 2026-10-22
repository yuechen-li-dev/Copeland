using Aurelian.Games;
using Cloudstep;
using Aurelian.Rendering.Contracts.Models;
using System.Text.Json;

Graphics3DSettings? researchLook = null;
int policyOption = Array.IndexOf(args, "--look-policy");
if (policyOption >= 0)
{
    if (policyOption + 1 >= args.Length)
    {
        throw new ArgumentException("--look-policy requires a settings JSON file.");
    }
    researchLook = JsonSerializer.Deserialize<Graphics3DSettings>(File.ReadAllText(args[policyOption + 1]),
        new JsonSerializerOptions { IncludeFields = true })
        ?? throw new InvalidDataException("The research look is empty.");
    researchLook.Validate();
}
GameApp.Run(new CloudstepGame(researchLook: researchLook), args);
