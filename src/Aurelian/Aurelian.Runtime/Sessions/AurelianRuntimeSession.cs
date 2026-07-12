using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

public sealed class AurelianRuntimeSession
{
    private const string RootStateName = "aurelian.runtime.session.m0.root";

    private readonly AurelianRuntimeSessionOptions _options;
    private readonly IAurelianAiWorldRunner _runner;
    private readonly ActuatorHost _actuatorHost;
    private readonly AiWorld _world;
    private AiAgent? _runtimeAgent;
    private bool _hasStopped;

    public AurelianRuntimeSession()
        : this(new AurelianRuntimeSessionOptions())
    {
    }

    public AurelianRuntimeSession(AurelianRuntimeSessionOptions? options)
    {
        _options = options ?? new AurelianRuntimeSessionOptions();
        _runner = _options.Runner ?? new SequentialAurelianAiWorldRunner();

        if (_options.World is not null)
        {
            _world = _options.World;
            _actuatorHost = _options.ActuatorHost
                ?? _world.Actuator as ActuatorHost
                ?? throw new ArgumentException("A provided AiWorld must use an ActuatorHost or be paired with the same ActuatorHost in options.", nameof(options));

            if (!ReferenceEquals(_world.Actuator, _actuatorHost))
                throw new ArgumentException("A provided AiWorld must use the same ActuatorHost supplied in options.", nameof(options));
        }
        else
        {
            _actuatorHost = _options.ActuatorHost ?? new ActuatorHost();
            _world = new AiWorld(_actuatorHost);
        }
    }

    public bool IsStarted { get; private set; }

    public AiWorld World => _world;

    public ActuatorHost ActuatorHost => _actuatorHost;

    public AurelianRuntimeResult Start()
    {
        if (IsStarted)
        {
            return AurelianRuntimeResult.Rejected(Diagnostic(
                AurelianRuntimeDiagnosticCodes.RuntimeAlreadyStarted,
                "Aurelian runtime session is already started."));
        }

        if (_hasStopped)
        {
            return AurelianRuntimeResult.Rejected(Diagnostic(
                AurelianRuntimeDiagnosticCodes.RuntimeAlreadyStopped,
                "Aurelian runtime session has already been stopped; create a new session for another run."));
        }

        _actuatorHost.Register(new DefaultRuntimeTickHandler());
        _options.ConfigureActuatorHost?.Invoke(_actuatorHost);

        var root = StateId.Of(RootStateName);
        var graph = new HfsmGraph { Root = root };
        graph.Add(root, RuntimeTickNode);

        _runtimeAgent = new AiAgent(new HfsmInstance(graph));
        _world.Add(_runtimeAgent);
        IsStarted = true;

        return AurelianRuntimeResult.Ok();
    }

    public AurelianRuntimeResult Stop()
    {
        if (!IsStarted)
        {
            return AurelianRuntimeResult.Rejected(Diagnostic(
                AurelianRuntimeDiagnosticCodes.RuntimeAlreadyStopped,
                _hasStopped
                    ? "Aurelian runtime session is already stopped."
                    : "Aurelian runtime session has not been started."));
        }

        IsStarted = false;
        _hasStopped = true;
        return AurelianRuntimeResult.Ok();
    }

    public async Task<AurelianRuntimeTickResult> TickAsync(
        AurelianRuntimeTickInput input,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Cancelled,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.Cancelled, "Aurelian runtime tick was cancelled."));
        }

        if (!IsStarted || _runtimeAgent is null)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Rejected,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.RuntimeNotStarted, "Aurelian runtime session must be started before ticking."));
        }

        if (input.DeltaTime < TimeSpan.Zero)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Rejected,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.InvalidDeltaTime, "Aurelian runtime tick delta time must be non-negative."));
        }

        _runtimeAgent.Bb.Set(AurelianRuntimeSessionKeys.Facts, new AurelianRuntimeSessionFacts(input.TickIndex, input.DeltaTime));

        try
        {
            await _runner.RunTickAsync(_world, input, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Cancelled,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.Cancelled, "Aurelian runtime tick was cancelled."));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Rejected,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.InvalidDeltaTime, ex.Message));
        }
        catch (Exception ex)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Failed,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.RunnerFailed, $"Aurelian runtime world runner failed: {ex.Message}"));
        }

        if (!_runtimeAgent.Bb.TryGet(AurelianRuntimeSessionKeys.TickActuationId, out ActuationId actuationId))
        {
            return TickResult(
                AurelianRuntimeTickStatus.Failed,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.DominatusTickFailed, "Dominatus runtime policy node did not emit a runtime tick act."));
        }

        if (!TryReadCompletion(_runtimeAgent, actuationId, out var completion))
        {
            return TickResult(
                AurelianRuntimeTickStatus.Failed,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.ActuationFailed, "Dominatus runtime tick act did not complete during the M0 runtime tick."));
        }

        if (!completion.Ok)
        {
            return TickResult(
                AurelianRuntimeTickStatus.Failed,
                input,
                Diagnostic(AurelianRuntimeDiagnosticCodes.ActuationFailed, completion.Error ?? "Dominatus runtime tick act failed."));
        }

        return new AurelianRuntimeTickResult(AurelianRuntimeTickStatus.Ticked, input.TickIndex, input.DeltaTime, []);
    }

    private static IEnumerator<AiStep> RuntimeTickNode(AiCtx ctx)
    {
        var facts = ctx.Agent.Bb.GetOrDefault(AurelianRuntimeSessionKeys.Facts, default!);
        if (facts is null)
        {
            yield return new Fail("No Aurelian runtime tick facts were supplied.");
            yield break;
        }

        yield return new Act(new AurelianRuntimeTickAct(facts.TickIndex), AurelianRuntimeSessionKeys.TickActuationId);
        yield return new AwaitActuation(AurelianRuntimeSessionKeys.TickActuationId);
        yield return new Succeed("AurelianRuntimeSessionM0TickComplete");
    }

    private static bool TryReadCompletion(AiAgent agent, ActuationId actuationId, out ActuationCompleted completion)
    {
        var cursor = default(EventCursor);
        return agent.Events.TryConsume(ref cursor, (ActuationCompleted e) => e.Id.Equals(actuationId), out completion);
    }

    private static AurelianRuntimeTickResult TickResult(
        AurelianRuntimeTickStatus status,
        AurelianRuntimeTickInput input,
        AurelianRuntimeDiagnostic diagnostic)
        => new(status, input.TickIndex, input.DeltaTime, [diagnostic]);

    private static AurelianRuntimeDiagnostic Diagnostic(
        string code,
        string message,
        AurelianRuntimeDiagnosticSeverity severity = AurelianRuntimeDiagnosticSeverity.Error)
        => new(code, severity, message);

    private sealed class DefaultRuntimeTickHandler : IActuationHandler<AurelianRuntimeTickAct>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, AurelianRuntimeTickAct cmd)
            => ActuatorHost.HandlerResult.CompletedOk();
    }
}
