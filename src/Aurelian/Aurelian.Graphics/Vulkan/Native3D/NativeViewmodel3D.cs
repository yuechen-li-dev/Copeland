using System.Numerics;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>
/// Explicit foreground geometry and camera. The host draws it after world output, with its own cleared depth.
/// It never enters world shadows, reflection captures, visibility, or temporal history.
/// </summary>
public sealed record NativeViewmodel3D(Native3DVertex[] Geometry, Matrix4x4 Camera, Vector3 Eye);
