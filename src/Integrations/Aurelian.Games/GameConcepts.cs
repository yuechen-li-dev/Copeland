using System.Security.Cryptography;
using System.Text;

namespace Aurelian.Games;

public enum GameConcept
{
    ThreeDimensional,
    FirstPersonCamera,
    FirstPersonControl,
    ThirdPersonCamera,
    ThirdPersonControl,
    ReloadableGuns,
}

/// <summary>Presets use exactly the same composition path as hand-written fragments.</summary>
public static class GamePresets
{
    public static IReadOnlyList<GameConcept> FirstPersonShooter =>
    [GameConcept.ThreeDimensional, GameConcept.FirstPersonCamera, GameConcept.FirstPersonControl, GameConcept.ReloadableGuns];

    public static IReadOnlyList<GameConcept> ThirdPersonShooter =>
    [GameConcept.ThreeDimensional, GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl, GameConcept.ReloadableGuns];
}

public sealed class GameDefinition
{
    private readonly HashSet<GameConcept> concepts;

    public GameDefinition(IEnumerable<GameConcept> concepts)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        this.concepts = concepts.ToHashSet();
        foreach (GameConcept concept in this.concepts)
        {
            if (!Enum.IsDefined(concept))
            {
                throw new ArgumentException($"Unsupported game concept '{concept}'.");
            }
        }
        Require(GameConcept.ThreeDimensional, "The current native starter requires ThreeDimensional.");
        if (!Has(GameConcept.FirstPersonCamera) && !Has(GameConcept.ThirdPersonCamera))
        {
            throw new ArgumentException("A 3D starter needs a camera fragment.");
        }
        RequirePair(GameConcept.FirstPersonCamera, GameConcept.FirstPersonControl);
        RequirePair(GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl);
        Concepts = Array.AsReadOnly(this.concepts.Order().ToArray());
        string canonical = "aurelian.starter.v1|" + string.Join('|', Concepts);
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public IReadOnlyList<GameConcept> Concepts { get; }
    public string Identity { get; }
    public bool Has(GameConcept concept) => concepts.Contains(concept);

    private void Require(GameConcept concept, string message)
    {
        if (!Has(concept))
        {
            throw new ArgumentException(message);
        }
    }

    private void RequirePair(GameConcept camera, GameConcept control)
    {
        if (Has(control) && !Has(camera))
        {
            throw new ArgumentException($"{control} requires {camera}.");
        }
    }
}
