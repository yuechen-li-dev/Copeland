using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Direction points toward the sun. Irradiance is the normal-incidence
/// directional-light coefficient, matching SunColor * SunIntensity in real-time settings.</summary>
public sealed record ReferenceSun3D(Vector3 Direction, Vector3 Irradiance);
public sealed record ReferenceLighting3D(ReferenceSun3D? Sun = null, EnvironmentRadiance3D? Environment = null,
    float EnvironmentIntensity = 1, bool ImportanceSampleEnvironment = true, bool ExcludePrimarySun = false, IReadOnlyList<LocalLight3D>? LocalLights = null)
{
    public static ReferenceLighting3D FromSettings(Graphics3DSettings settings, EnvironmentRadiance3D? environment = null)
    {
        settings.Validate();
        return new(new(settings.SunDirection, settings.SunColor * settings.SunIntensity), environment, settings.EnvironmentIntensity);
    }

    internal float[] Distribution()
    {
        return Environment?.ImportanceCdf.ToArray() ?? [];
    }
}
