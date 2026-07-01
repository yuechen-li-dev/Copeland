namespace Aurelian.Rendering.Contracts.Compositor;

public readonly record struct PlantOutputRef(
    uint PlantId,
    ulong FrameId,
    string ImageId)
{
    public override string ToString()
        => $"{PlantId}:{FrameId}:{ImageId}";
}
