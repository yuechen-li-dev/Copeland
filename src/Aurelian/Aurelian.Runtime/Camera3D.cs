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
}
