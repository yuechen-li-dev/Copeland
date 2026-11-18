using System.Numerics;

namespace Aurelian.Runtime;

public enum CameraView { FirstPerson, ThirdPerson }

public static class Camera3D
{
    public static Vector3 Direction(float yaw, float pitch) =>
        new(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));

    public static Vector3 Eye(Vector3 actorFeet, float yaw, float pitch, CameraView view, float followDistance = 4)
    {
        Vector3 head = actorFeet + Vector3.UnitY * 1.6f;
        if (view == CameraView.FirstPerson)
        {
            return head;
        }
        return head - Direction(yaw, pitch) * followDistance + Vector3.UnitY * 0.5f;
    }

    public static Matrix4x4 Matrix(Vector3 eye, Vector3 direction, float aspect, float far = 80)
    {
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, aspect, 0.1f, far);
        projection.M22 = -projection.M22;
        return Matrix4x4.CreateLookAt(eye, eye + direction, Vector3.UnitY) * projection;
    }

    /// <summary>Projects a world point to pixels using clip coordinates with Y down and depth from zero to one.</summary>
    public static bool TryProject(Vector3 world, Matrix4x4 camera, Vector2 surface, out Vector2 pixel)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1), camera);
        pixel = default;
        if (clip.W <= 0 || clip.Z < 0 || clip.Z > clip.W)
        {
            return false;
        }
        pixel = (new Vector2(clip.X, clip.Y) / clip.W + Vector2.One) * surface * .5f;
        return true;
    }

    /// <summary>Intersects the screen ray with a horizontal plane using clip coordinates with Y down and depth from zero to one.</summary>
    public static bool TryPickPlane(Vector2 pixel, Vector2 surface, Matrix4x4 camera, float height, out Vector3 world)
    {
        world = default;
        if (surface.X <= 0 || surface.Y <= 0 || !Matrix4x4.Invert(camera, out Matrix4x4 inverse))
        {
            return false;
        }
        Vector2 clip = pixel / surface * 2 - Vector2.One;
        Vector4 near = Vector4.Transform(new Vector4(clip, 0, 1), inverse);
        Vector4 far = Vector4.Transform(new Vector4(clip, 1, 1), inverse);
        if (near.W == 0 || far.W == 0)
        {
            return false;
        }
        Vector3 origin = new Vector3(near.X, near.Y, near.Z) / near.W;
        Vector3 direction = new Vector3(far.X, far.Y, far.Z) / far.W - origin;
        if (MathF.Abs(direction.Y) < 1e-6f)
        {
            return false;
        }
        float fraction = (height - origin.Y) / direction.Y;
        if (!(fraction >= 0 && fraction <= 1))
        {
            return false;
        }
        world = origin + fraction * direction;
        return true;
    }
}
