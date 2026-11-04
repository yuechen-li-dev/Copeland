namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Canonical local surfaces. Instance transforms supply placement, scale and shear.</summary>
public enum AnalyticPrimitiveKind3D
{
    Triangles = 0,
    UnitSphere = 1,
    UnitBox = 2,
    Capsule = 3,
}
