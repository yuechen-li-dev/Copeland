using System.Numerics;
using Aetheris.Humanoid;
using Aurelian.Runtime.Dominatus.Inspection;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Aurelian.Humanoid;

public sealed record CharacterPresentationObservation(float Speed, bool Aiming, bool Grounded = true);
public sealed record HumanoidCharacterState(Vector3 Position, float Heading, CharacterAnimationState Animation);

public static partial class CharacterPresentationFlow
{
    internal static readonly BbKey<float> Speed = new("Character.Observation.Speed");
    internal static readonly BbKey<bool> Aiming = new("Character.Observation.Aiming");
    internal static readonly BbKey<bool> Grounded = new("Character.Observation.Grounded");
    internal static readonly BbKey<string> Motion = new("Character.Presentation.Motion");
    internal static readonly BbKey<float> Phase = new("Character.Presentation.ClipSeconds");
    public static FlowDefinition Definition { get; } = Define();

    [DominatusFlow("aurelian.character.presentation", KeepRootFrame = true)]
    public static partial FlowDefinition Define();

    [DominatusState("Observe", Root = true)]
    private static IEnumerator<AiStep> Observe(AiCtx context)
    {
        while (true)
        {
            yield return Ai.Decide(new DecisionSlot("Character.Presentation"),
            [
                Ai.Option("Aim", new Consideration((_, agent) => agent.Bb.GetOrDefault(Aiming, false) ? 2 : 0), States.Aim),
                Ai.Option("Walk", new Consideration((_, agent) =>
                    agent.Bb.GetOrDefault(Grounded, true) && agent.Bb.GetOrDefault(Speed, 0) > .05f ? 1 : 0), States.Walk),
                Ai.Option("Idle", Consideration.Constant(.1f), States.Idle),
            ], hysteresis: 0, minCommitSeconds: 0, tieEpsilon: .0001f);
        }
    }

    [DominatusState("Idle")]
    private static IEnumerator<AiStep> Idle(AiCtx context)
    {
        context.Bb.Set(Motion, nameof(CharacterMotion.Idle));
        yield return Ai.Succeed();
    }
    [DominatusState("Walk")]
    private static IEnumerator<AiStep> Walk(AiCtx context)
    {
        context.Bb.Set(Motion, nameof(CharacterMotion.Walk));
        yield return Ai.Succeed();
    }
    [DominatusState("Aim")]
    private static IEnumerator<AiStep> Aim(AiCtx context)
    {
        context.Bb.Set(Motion, nameof(CharacterMotion.Aim));
        yield return Ai.Succeed();
    }
}

public sealed record HumanoidCharacterDefinition(HumanoidGameplayBody Body, AurelianAgentRuntime Policies,
    HumanoidAnimationSet Animations) : AgentDefinition<HumanoidCharacterState>(AgentTemplate.Character("humanoid.v1"))
{
    public override string Identity => Body.Id + ".character.v1";
    public override HumanoidCharacterState CreateState(ScenePlacement placement) =>
        new(placement.WorldTransform.Translation,
            MathF.Atan2(placement.WorldTransform.M31, placement.WorldTransform.M33), Animations.Initial);
    public override void ValidatePlacement(ScenePlacement placement)
    {
        SceneTransform.ValidateMatrix(placement.WorldTransform);
        Matrix4x4 world = placement.WorldTransform;
        var basisX = new Vector3(world.M11, world.M12, world.M13);
        var basisY = new Vector3(world.M21, world.M22, world.M23);
        var basisZ = new Vector3(world.M31, world.M32, world.M33);
        if (MathF.Abs(basisX.LengthSquared() - 1) > .0001f ||
            Vector3.Distance(basisY, Vector3.UnitY) > .0001f ||
            MathF.Abs(basisZ.LengthSquared() - 1) > .0001f ||
            MathF.Abs(Vector3.Dot(basisX, basisZ)) > .0001f)
            throw new InvalidDataException("Humanoid agents require upright rigid placements; customize heading and position explicitly.");
    }
    public override IDisposable Activate(SceneAgent<HumanoidCharacterState> agent) =>
        ScenePolicyBinding.Bind(agent, Policies, CharacterPresentationFlow.Definition.CreateBrain);
    public override Matrix4x4 WorldTransform(HumanoidCharacterState state, ScenePlacement placement) =>
        Matrix4x4.CreateRotationY(state.Heading) * Matrix4x4.CreateTranslation(state.Position);

    public void Observe(SceneAgent<HumanoidCharacterState> agent, CharacterPresentationObservation observation)
    {
        if (!float.IsFinite(observation.Speed) || observation.Speed < 0)
            throw new ArgumentOutOfRangeException(nameof(observation));
        var kernel = Policies.Agent(agent.Id);
        kernel.Bb.Set(CharacterPresentationFlow.Speed, observation.Speed);
        kernel.Bb.Set(CharacterPresentationFlow.Aiming, observation.Aiming);
        kernel.Bb.Set(CharacterPresentationFlow.Grounded, observation.Grounded);
    }

    /// <summary>Call after the shared Dominatus runtime tick. Rendering never advances this state.</summary>
    public void Advance(SceneAgent<HumanoidCharacterState> agent, double seconds)
    {
        var kernel = Policies.Agent(agent.Id);
        var motion = Enum.Parse<CharacterMotion>(kernel.Bb.GetOrDefault(CharacterPresentationFlow.Motion, "Idle"));
        var animation = Animations.Advance(agent.State.Animation, motion, seconds,
            kernel.Bb.GetOrDefault(CharacterPresentationFlow.Speed, 0));
        agent.State = agent.State with { Animation = animation };
        kernel.Bb.Set(CharacterPresentationFlow.Phase, (float)animation.ClipSeconds);
    }

    public SolvedHumanoidPose Pose(SceneAgent<HumanoidCharacterState> agent)
    {
        var solved = Body.Solve(agent.Id + "." + agent.State.Animation.Motion, Animations.Sample(agent.State.Animation));
        if (!solved.IsSolved)
            throw new InvalidDataException(string.Join("; ", solved.Diagnostics.Select(item => item.Message)));
        return solved.Pose!;
    }
}
