using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Spatial3D;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Aurelian.Physics3D.Bepu;

/// <summary>Optional CPU backend. No BEPU handles or mutable simulation objects escape this owner.</summary>
public sealed partial class BepuPhysicsWorld3D : IPhysicsWorld3D
{
    private sealed record Entry(PhysicsBody3D Description, TypedIndex Shape, int Handle);

    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly BufferPool pool = new();
    private readonly Simulation simulation;
    private readonly ThreadDispatcher? dispatcher;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, PhysicsBody3D> mobileBodies = new();
    private readonly Dictionary<int, PhysicsBody3D> staticBodies = new();
    private readonly HashSet<PhysicsContactPair3D>[] workerContacts;
    private readonly HashSet<PhysicsContactPair3D> combinedContacts = new();
    private static readonly IComparer<PhysicsContactPair3D> ContactOrder =
        Comparer<PhysicsContactPair3D>.Create(CompareContacts);
    private bool disposed;

    public BepuPhysicsWorld3D(PhysicsWorldOptions3D? options = null)
    {
        Options = options ?? new();
        Options.Validate();
        workerContacts = Enumerable.Range(0, Options.WorkerCount)
            .Select(_ => new HashSet<PhysicsContactPair3D>()).ToArray();
        simulation = Simulation.Create(pool, new NarrowPhase(this), new Integrator(Options.Gravity),
            new SolveDescription(Options.SolverIterations, Options.Substeps));
        if (Options.WorkerCount > 1)
        {
            dispatcher = new ThreadDispatcher(Options.WorkerCount);
        }
        // Reproducibility on the same backend/runtime; not a cross-platform lockstep promise.
        simulation.Deterministic = true;
    }

    public string Backend => "bepu2/2.4.0";
    public PhysicsWorldOptions3D Options { get; }
    public long Tick { get; private set; }
    public int BodyCount => entries.Count;
    /// <summary>Main pool reservation only; excludes dispatcher worker pools and managed allocations.</summary>
    public long ReservedMainPoolBytes
    {
        get
        {
            RequireLive();
            long reserved = 0;
            for (int power = 0; power <= SpanHelper.MaximumSpanSizePower; power++)
            {
                reserved += pool.GetCapacityForPower(power);
            }
            return reserved;
        }
    }

    public void AddBody(PhysicsBody3D body)
    {
        RequireLive();
        ArgumentNullException.ThrowIfNull(body);
        body.Validate();
        if (entries.ContainsKey(body.Id))
        {
            throw new ArgumentException($"Physics body '{body.Id}' already exists.", nameof(body));
        }
        TypedIndex shape = AddShape(body.Shape, body.Mass, out BodyInertia inertia);
        var pose = new RigidPose(body.Pose.Position, body.Pose.Orientation);
        ContinuousDetection continuity = Continuity(body);
        int handle;
        if (body.MotionType == PhysicsMotionType3D.Static)
        {
            handle = simulation.Statics.Add(new StaticDescription(pose, shape, continuity)).Value;
            staticBodies.Add(handle, body);
        }
        else
        {
            var velocity = new BodyVelocity(body.Velocity.Linear, body.Velocity.Angular);
            var collidable = new CollidableDescription(shape, body.MaximumSpeculativeMargin, continuity);
            var activity = new BodyActivityDescription(Options.EnableSleeping ? .01f : -1);
            BodyDescription description;
            if (body.MotionType == PhysicsMotionType3D.Dynamic)
            {
                description = BodyDescription.CreateDynamic(pose, velocity, inertia, collidable, activity);
            }
            else
            {
                description = BodyDescription.CreateKinematic(pose, velocity, collidable, activity);
            }
            handle = simulation.Bodies.Add(description).Value;
            mobileBodies.Add(handle, body);
        }
        entries.Add(body.Id, new(body, shape, handle));
    }

    public bool RemoveBody(string id)
    {
        RequireLive();
        if (!entries.TryGetValue(id, out Entry? entry))
        {
            return false;
        }
        foreach (string jointId in joints.Values.Where(joint => joint.Description.BodyA == id
            || joint.Description.BodyB == id).Select(joint => joint.Description.Id).ToArray())
        {
            RemoveJoint(jointId);
        }
        excludedPairs.RemoveWhere(pair => pair.BodyA == id || pair.BodyB == id);
        entries.Remove(id);
        if (entry.Description.MotionType == PhysicsMotionType3D.Static)
        {
            simulation.Statics.Remove(new StaticHandle(entry.Handle));
            staticBodies.Remove(entry.Handle);
        }
        else
        {
            simulation.Bodies.Remove(new BodyHandle(entry.Handle));
            mobileBodies.Remove(entry.Handle);
        }
        simulation.Shapes.RemoveAndDispose(entry.Shape, pool);
        return true;
    }

    public PhysicsBodyState3D GetBody(string id)
    {
        RequireLive();
        Entry entry = entries[id];
        if (entry.Description.MotionType == PhysicsMotionType3D.Static)
        {
            return new(id, entry.Description.SemanticOwnerId, entry.Description.MotionType,
                entry.Description.Pose, default, false);
        }
        BodyReference body = simulation.Bodies.GetBodyReference(new BodyHandle(entry.Handle));
        return new(id, entry.Description.SemanticOwnerId, entry.Description.MotionType,
            new(body.Pose.Position, body.Pose.Orientation), new(body.Velocity.Linear, body.Velocity.Angular), body.Awake);
    }

    public void SetMotion(string id, PhysicsPose3D pose, PhysicsVelocity3D velocity)
    {
        RequireLive();
        pose.Validate();
        velocity.Validate();
        Entry entry = RequireMobile(id);
        BodyReference body = simulation.Bodies.GetBodyReference(new BodyHandle(entry.Handle));
        body.Awake = true;
        body.Pose = new RigidPose(pose.Position, pose.Orientation);
        body.Velocity = new BodyVelocity(velocity.Linear, velocity.Angular);
        body.UpdateBounds();
    }

    public bool TryGetBody(string id, out PhysicsBodyState3D? body)
    {
        RequireLive();
        body = entries.ContainsKey(id) ? GetBody(id) : null;
        return body is not null;
    }

    public PhysicsBody3D GetBodyDescription(string id)
    {
        RequireLive();
        return entries[id].Description;
    }

    public void ApplyImpulse(string id, Vector3 impulse, Vector3 worldOffset = default)
    {
        RequireLive();
        new PhysicsVelocity3D(impulse, worldOffset).Validate();
        Entry entry = RequireMobile(id);
        if (entry.Description.MotionType != PhysicsMotionType3D.Dynamic)
        {
            throw new InvalidOperationException("Only dynamic bodies accept impulses.");
        }
        BodyReference body = simulation.Bodies.GetBodyReference(new BodyHandle(entry.Handle));
        body.Awake = true;
        body.ApplyImpulse(impulse, worldOffset);
    }

    public PhysicsStepResult3D Step(PhysicsStepRequest3D request)
    {
        RequireLive();
        if (request.Tick != checked(Tick + 1))
        {
            throw new ArgumentException($"Expected physics tick {Tick + 1}, received {request.Tick}.", nameof(request));
        }
        foreach (HashSet<PhysicsContactPair3D> contacts in workerContacts)
        {
            contacts.Clear();
        }
        simulation.Timestep(Options.FixedDeltaSeconds, dispatcher);
        Tick = request.Tick;
        combinedContacts.Clear();
        foreach (HashSet<PhysicsContactPair3D> contacts in workerContacts)
        {
            combinedContacts.UnionWith(contacts);
        }
        var orderedContacts = ImmutableArray.CreateBuilder<PhysicsContactPair3D>(combinedContacts.Count);
        orderedContacts.AddRange(combinedContacts);
        orderedContacts.Sort(ContactOrder);
        return new(Tick, orderedContacts.MoveToImmutable());
    }

    public PhysicsSnapshot3D CaptureSnapshot()
    {
        RequireLive();
        return new(Backend, Tick, entries.Keys.Order(StringComparer.Ordinal).Select(GetBody).ToImmutableArray());
    }

    public PhysicsRayHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null)
    {
        RequireLive();
        ray.Validate();
        var handler = new RayHandler(this, filter ?? QueryFilter3D.All);
        simulation.RayCast(ray.Origin, ray.Direction, ray.MaximumDistance, ref handler);
        return handler.Hit;
    }

    private Entry RequireMobile(string id)
    {
        Entry entry = entries[id];
        if (entry.Description.MotionType == PhysicsMotionType3D.Static)
        {
            throw new InvalidOperationException("Static bodies are fixed; remove/re-add them or author a kinematic body.");
        }
        return entry;
    }

    private PhysicsBody3D Describe(CollidableReference collidable)
    {
        if (collidable.Mobility == CollidableMobility.Static)
        {
            return staticBodies[collidable.StaticHandle.Value];
        }
        return mobileBodies[collidable.BodyHandle.Value];
    }

    private TypedIndex AddShape(PhysicsShape3D shape, float mass, out BodyInertia inertia)
    {
        switch (shape)
        {
            case PhysicsShape3D.Box source:
                var box = new Box(source.Size.X, source.Size.Y, source.Size.Z);
                inertia = box.ComputeInertia(mass);
                return simulation.Shapes.Add(box);
            case PhysicsShape3D.Sphere source:
                var sphere = new Sphere(source.Radius);
                inertia = sphere.ComputeInertia(mass);
                return simulation.Shapes.Add(sphere);
            case PhysicsShape3D.Capsule source:
                var capsule = new Capsule(source.Radius, source.Length);
                inertia = capsule.ComputeInertia(mass);
                return simulation.Shapes.Add(capsule);
            case PhysicsShape3D.StaticMesh source:
                CollisionMesh3D mesh = source.Geometry;
                pool.Take<Triangle>(mesh.Indices.Length / 3, out var triangles);
                for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
                {
                    int offset = triangleIndex * 3;
                    // Aurelian uses outward counterclockwise faces; BEPU's triangles face clockwise.
                    triangles[triangleIndex] = new Triangle(mesh.Positions[mesh.Indices[offset]],
                        mesh.Positions[mesh.Indices[offset + 2]], mesh.Positions[mesh.Indices[offset + 1]]);
                }
                inertia = default;
                return simulation.Shapes.Add(new Mesh(triangles, Vector3.One, pool));
            default:
                throw new NotSupportedException("Unsupported physics shape.");
        }
    }

    private void RequireLive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("Physics commands and inspection must run on their owning thread.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        RequireLive();
        disposed = true;
        simulation.Dispose();
        dispatcher?.Dispose();
        pool.Clear();
        entries.Clear();
        joints.Clear();
        excludedPairs.Clear();
        jointExclusions.Clear();
        mobileBodies.Clear();
        staticBodies.Clear();
        combinedContacts.Clear();
        foreach (HashSet<PhysicsContactPair3D> contacts in workerContacts)
        {
            contacts.Clear();
        }
    }

    private static int CompareContacts(PhysicsContactPair3D first, PhysicsContactPair3D second)
    {
        int order = StringComparer.Ordinal.Compare(first.BodyA, second.BodyA);
        if (order != 0)
        {
            return order;
        }
        return StringComparer.Ordinal.Compare(first.BodyB, second.BodyB);
    }

    private readonly struct NarrowPhase(BepuPhysicsWorld3D owner) : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public void Dispose() { }

        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin)
        {
            if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic)
            {
                return false;
            }
            PhysicsBody3D first = owner.Describe(a);
            PhysicsBody3D second = owner.Describe(b);
            PhysicsContactPair3D pair = CanonicalPair(first.Id, second.Id);
            return (first.Layer & second.Mask) != 0 && (second.Layer & first.Mask) != 0
                && !owner.excludedPairs.Contains(pair) && !owner.jointExclusions.ContainsKey(pair);
        }

        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
        {
            return true;
        }

        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair,
            ref TManifold manifold, out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            PhysicsBody3D first = owner.Describe(pair.A);
            PhysicsBody3D second = owner.Describe(pair.B);
            pairMaterial = new PairMaterialProperties
            {
                FrictionCoefficient = MathF.Sqrt(first.Friction) * MathF.Sqrt(second.Friction),
                MaximumRecoveryVelocity = owner.Options.MaximumRecoveryVelocity,
                SpringSettings = new SpringSettings(owner.Options.ContactSpring.Frequency, owner.Options.ContactSpring.DampingRatio),
            };
            if (owner.Options.CollectContacts)
            {
                for (int index = 0; index < manifold.Count; index++)
                {
                    manifold.GetContact(index, out _, out _, out float depth, out _);
                    if (depth >= 0)
                    {
                        var contact = StringComparer.Ordinal.Compare(first.Id, second.Id) <= 0
                            ? new PhysicsContactPair3D(first.Id, second.Id)
                            : new PhysicsContactPair3D(second.Id, first.Id);
                        // Each BEPU worker owns one retained set; callbacks allocate no queue nodes.
                        owner.workerContacts[workerIndex].Add(contact);
                        break;
                    }
                }
            }
            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA,
            int childIndexB, ref ConvexContactManifold manifold)
        {
            return true;
        }
    }

    private struct Integrator(Vector3 gravity) : IPoseIntegratorCallbacks
    {
        private Vector3Wide gravityDelta;
        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt)
        {
            gravityDelta = Vector3Wide.Broadcast(gravity * dt);
        }

        public readonly void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position,
            QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask,
            int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            velocity.Linear += gravityDelta;
        }
    }

    private struct RayHandler(BepuPhysicsWorld3D owner, QueryFilter3D filter) : IRayHitHandler
    {
        public PhysicsRayHit3D? Hit;

        public readonly bool AllowTest(CollidableReference collidable)
        {
            PhysicsBody3D body = owner.Describe(collidable);
            return filter.Matches(body.Layer, body.Mask);
        }

        public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal,
            CollidableReference collidable, int childIndex)
        {
            PhysicsBody3D body = owner.Describe(collidable);
            if (Hit is { } previous && (t > previous.Distance
                || (t == previous.Distance && StringComparer.Ordinal.Compare(body.Id, previous.BodyId) >= 0)))
            {
                return;
            }
            maximumT = t;
            Hit = new(body.Id, body.SemanticOwnerId, t, ray.Origin + ray.Direction * t, normal);
        }
    }
}
