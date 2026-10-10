using System.Numerics;
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

/// <summary>A tiny direct-BEPU control isolates adapter mistakes from backend/profile behavior.</summary>
internal static class PhysicsRawCcdControl
{
    internal static IEnumerable<PhysicsQualificationLab.Result> Measure()
    {
        foreach (var profile in new[]
        {
            (Substeps: 1, Hertz: 30f, Sweep: .0001f),
            (Substeps: 4, Hertz: 300f, Sweep: .0001f),
            (Substeps: 4, Hertz: 1000f, Sweep: .000001f),
        })
        {
            using var adapter = new BepuPhysicsWorld3D(new()
            {
                Gravity = Vector3.Zero,
                Substeps = profile.Substeps,
                ContactSpring = new(profile.Hertz, 1),
            });
            adapter.AddBody(new("wall", new PhysicsShape3D.Box(new(.01f, 10, 10)), PhysicsPose3D.At(Vector3.Zero))
            {
                MotionType = PhysicsMotionType3D.Static,
            });
            adapter.AddBody(new("shot", new PhysicsShape3D.Sphere(.05f), PhysicsPose3D.At(new(-1, 0, 0)))
            {
                Continuity = PhysicsContinuity3D.Continuous,
                MinimumSweepSeconds = profile.Sweep,
                SweepConvergenceSeconds = profile.Sweep,
                Velocity = new(new(500, 0, 0), Vector3.Zero),
            });
            adapter.Step(new(1));
            var pool = new BufferPool();
            float rawX;
            float rawVelocity;
            try
            {
                using var simulation = Simulation.Create(pool, new Contacts(profile.Hertz), new NoForces(), new SolveDescription(8, profile.Substeps));
                simulation.Deterministic = true;
                simulation.Statics.Add(new StaticDescription(Vector3.Zero, simulation.Shapes.Add(new Box(.01f, 10, 10))));
                var sphere = new Sphere(.05f);
                var handle = simulation.Bodies.Add(BodyDescription.CreateDynamic(new RigidPose(new Vector3(-1, 0, 0)),
                    new BodyVelocity(new Vector3(500, 0, 0)), sphere.ComputeInertia(1),
                    new CollidableDescription(simulation.Shapes.Add(sphere), .1f,
                        ContinuousDetection.Continuous(profile.Sweep, profile.Sweep)), new BodyActivityDescription(.01f)));
                simulation.Timestep(1f / 60);
                BodyReference body = simulation.Bodies.GetBodyReference(handle);
                rawX = body.Pose.Position.X;
                rawVelocity = body.Velocity.Linear.X;
            }
            finally
            {
                pool.Clear();
            }
            var owned = adapter.GetBody("shot");
            double error = Math.Max(Math.Abs(rawX - owned.Pose.Position.X), Math.Abs(rawVelocity - owned.Velocity.Linear.X));
            yield return new("ccd", $"raw-BEPU-control/{profile.Substeps}sub/{profile.Hertz}Hz/sweep-{profile.Sweep}", error < .00001,
                "Direct BEPU and owned adapter agree within 1e-5 in first-step x and vx; this is adapter agreement, not wall-blocking acceptance.",
                new() { ["RawX"] = rawX, ["RawVelocityX"] = rawVelocity, ["AdapterX"] = owned.Pose.Position.X, ["MaximumDifference"] = error });
        }
    }

    private readonly struct Contacts(float hertz) : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public void Dispose() { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) => true;
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new() { FrictionCoefficient = .8f, MaximumRecoveryVelocity = 2, SpringSettings = new(hertz, 1) };
            return true;
        }
    }

    private struct NoForces : IPoseIntegratorCallbacks
    {
        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }
        public void PrepareForIntegration(float dt) { }
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity) { }
    }
}
