using Aurelian.Games;
using Cloudstep;
using Aurelian.Rendering.Contracts.Models;
using System.Text.Json;

Graphics3DSettings? researchLook = null;
CloudstepLookProfile? researchProfile = null;
int profileOption = Array.IndexOf(args, "--look-profile");
int policyOption = Array.IndexOf(args, "--look-policy");
if (profileOption >= 0)
{
    if (policyOption >= 0 || profileOption + 1 >= args.Length)
    {
        throw new ArgumentException("--look-profile requires one profile JSON file and cannot be combined with --look-policy.");
    }
    researchProfile = JsonSerializer.Deserialize<CloudstepLookProfile>(File.ReadAllText(args[profileOption + 1]),
        new JsonSerializerOptions { IncludeFields = true })
        ?? throw new InvalidDataException("The research profile is empty.");
    researchProfile.Validate();
}
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
GameApp.Run(new CloudstepGame(researchLook: researchLook, researchProfile: researchProfile), args);
