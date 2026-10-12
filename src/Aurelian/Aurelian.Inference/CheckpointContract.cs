using Aurelian.Assets.Tensors;

namespace Aurelian.Inference;

public sealed record ModelTensorRequirement(string Name, IReadOnlyList<int> Shape,
    string? DataType = null, bool Optional = false);

/// <summary>Author-supplied model schema, checked against metadata before GPU
/// execution. This validates explicit names/layouts; it does not guess architecture.</summary>
public static class CheckpointContract
{
    public static void Validate(ICheckpointWeights weights, params ModelTensorRequirement[] requirements)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Length is < 1 or > 32768)
        {
            throw Stop("A model contract needs 1..32768 tensor requirements.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (requirement is null || string.IsNullOrWhiteSpace(requirement.Name)
                || !names.Add(requirement.Name) || requirement.Shape is null
                || requirement.Shape.Count is < 1 or > 5 || requirement.Shape.Any(value => value < 1))
            {
                throw Stop("Model tensor requirements must have unique names and positive rank-1..5 shapes.");
            }
            if (!weights.Tensors.TryGetValue(requirement.Name, out var tensor))
            {
                if (requirement.Optional)
                {
                    continue;
                }
                throw Stop("Missing required tensor: " + requirement.Name);
            }
            if (!tensor.Shape.SequenceEqual(requirement.Shape)
                || requirement.DataType is not null && requirement.DataType != tensor.DataType)
            {
                throw Stop("Tensor shape or encoding differs from its authored contract: " + requirement.Name);
            }
        }
    }

    private static InvalidDataException Stop(string message) => new("AUR-INFERENCE-WEIGHTS-001: " + message);
}
