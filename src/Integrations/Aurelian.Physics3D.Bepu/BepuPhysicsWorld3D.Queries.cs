using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Spatial3D;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace Aurelian.Physics3D.Bepu;

public sealed partial class BepuPhysicsWorld3D
{
    public ISpatialQueryWorld3D CreateQueryWorld(params string[] excludedBodyIds)
    {
        RequireLive();
        ArgumentNullException.ThrowIfNull(excludedBodyIds);
        foreach (string id in excludedBodyIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
        }
        return new QueryView(this, excludedBodyIds.ToImmutableHashSet(StringComparer.Ordinal));
    }

    private sealed class QueryView(BepuPhysicsWorld3D owner, ImmutableHashSet<string> excluded) : ISpatialQueryWorld3D
    {
        internal bool Allows(CollidableReference reference, QueryFilter3D filter)
        {
            PhysicsBody3D body = owner.Describe(reference);
            return !excluded.Contains(body.Id) && filter.Matches(body.Layer, body.Mask);
        }

        public SpatialHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null)
        {
            owner.RequireLive();
            ray.Validate();
            var handler = new ViewRayHandler(this, filter ?? QueryFilter3D.All, ray.MaximumDistance);
            owner.simulation.RayCast(ray.Origin, ray.Direction, ray.MaximumDistance, ref handler);
            return handler.Hit;
        }

        public ImmutableArray<SpatialHit3D> Overlap(Capsule3D shape, QueryFilter3D? filter = null)
        {
            owner.RequireLive();
            shape.Validate();
            var handler = new ViewSweepHandler(this, filter ?? QueryFilter3D.All, Vector3.Zero, (shape.A + shape.B) * .5f, true);
            Cast(shape, Vector3.Zero, ref handler);
            return handler.Overlaps.Values.OrderBy(hit => hit.ColliderId, StringComparer.Ordinal).ToImmutableArray();
        }

        public SpatialHit3D? Sweep(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null)
        {
            owner.RequireLive();
            shape.Validate();
            new PhysicsVelocity3D(displacement, default).Validate();
            var handler = new ViewSweepHandler(this, filter ?? QueryFilter3D.All, displacement, (shape.A + shape.B) * .5f, false);
            Cast(shape, displacement, ref handler);
            return handler.Hit;
        }

        private void Cast(Capsule3D shape, Vector3 displacement, ref ViewSweepHandler handler)
        {
            Vector3 segment = shape.B - shape.A;
            float length = segment.Length();
            Quaternion orientation = Quaternion.Identity;
            if (length > 1e-8f)
            {
                Vector3 axis = segment / length;
                float dot = Vector3.Dot(Vector3.UnitY, axis);
                if (dot < -.999999f)
                {
                    orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
                }
                else
                {
                    orientation = Quaternion.Normalize(new Quaternion(Vector3.Cross(Vector3.UnitY, axis), 1 + dot));
                }
            }
            var pose = new RigidPose((shape.A + shape.B) * .5f, orientation);
            var velocity = new BodyVelocity(displacement);
            // Match the shared query world's contact tolerance; exact tangency is not an initial penetration.
            float radius = shape.Radius - Math.Min(.00001f, shape.Radius * .001f);
            // Cast parameter is a unit displacement interval, not the simulation's timestep.
            if (length < 1e-8f)
            {
                owner.simulation.Sweep(new Sphere(radius), pose, velocity, 1, owner.pool, ref handler, 1e-6f, 1e-6f, 64);
            }
            else
            {
                owner.simulation.Sweep(new Capsule(radius, length), pose, velocity, 1, owner.pool, ref handler, 1e-6f, 1e-6f, 64);
            }
        }

        internal SpatialHit3D Contact(CollidableReference reference, float time, float distance, Vector3 point,
            Vector3 normal, SpatialQueryStatus3D status, int triangle = -1)
        {
            PhysicsBody3D body = owner.Describe(reference);
            CollisionMesh3D? mesh = (body.Shape as PhysicsShape3D.StaticMesh)?.Geometry;
            string? face = mesh is not null && triangle >= 0 && triangle < mesh.TriangleFaces.Length ? mesh.TriangleFaces[triangle] : null;
            return new(body.Id, mesh is null ? -1 : triangle, distance, time, point, normal, status, body.SemanticOwnerId,
                mesh?.SourceIdentity, face);
        }

        public MoveResult3D SweepAndSlide(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null,
            int maximumIterations = 6, float minimumGroundNormalY = 0)
        {
            owner.RequireLive();
            return SpatialMovement3D.SweepAndSlide(this, shape, displacement, filter, maximumIterations, minimumGroundNormalY);
        }
    }

    private struct ViewRayHandler(QueryView view, QueryFilter3D filter, float length) : IRayHitHandler
    {
        public SpatialHit3D? Hit;
        public readonly bool AllowTest(CollidableReference reference) => view.Allows(reference, filter);
        public readonly bool AllowTest(CollidableReference reference, int childIndex) => true;
        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference reference, int childIndex)
        {
            var candidate = view.Contact(reference, t / length, t, ray.Origin + ray.Direction * t,
                normal, SpatialQueryStatus3D.Contact, childIndex);
            if (Better(candidate, Hit))
            {
                Hit = candidate;
                maximumT = t + 1e-6f;
            }
        }
    }

    private struct ViewSweepHandler(QueryView view, QueryFilter3D filter, Vector3 displacement, Vector3 center, bool overlapOnly) : ISweepHitHandler
    {
        public SpatialHit3D? Hit;
        public readonly Dictionary<string, SpatialHit3D> Overlaps = new(StringComparer.Ordinal);
        public readonly bool AllowTest(CollidableReference reference) => view.Allows(reference, filter);
        public readonly bool AllowTest(CollidableReference reference, int childIndex) => true;
        public void OnHit(ref float maximumT, float t, in Vector3 location, in Vector3 normal, CollidableReference reference)
        {
            if (overlapOnly) return;
            Vector3 outward = Vector3.Dot(normal, displacement) > 0 ? -normal : normal;
            SpatialHit3D candidate = view.Contact(reference, t, t * displacement.Length(), location, outward, SpatialQueryStatus3D.Contact);
            if (Better(candidate, Hit))
            {
                Hit = candidate;
                maximumT = Math.Min(1, t + 1e-6f);
            }
        }
        public void OnHitAtZeroT(ref float maximumT, CollidableReference reference)
        {
            SpatialHit3D candidate = view.Contact(reference, 0, 0, center, Vector3.Zero, SpatialQueryStatus3D.InitiallyOverlapping);
            Overlaps.TryAdd(candidate.ColliderId, candidate);
            if (!overlapOnly && Better(candidate, Hit))
            {
                Hit = candidate;
                maximumT = 1e-6f;
            }
        }
    }

    private static bool Better(SpatialHit3D candidate, SpatialHit3D? current)
    {
        if (current is null) return true;
        float difference = candidate.Distance - current.Value.Distance;
        return difference < -1e-6f || (Math.Abs(difference) <= 1e-6f
            && StringComparer.Ordinal.Compare(candidate.ColliderId, current.Value.ColliderId) < 0);
    }
}
