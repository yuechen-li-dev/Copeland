using Aurelian.Simulation;

namespace Aurelian.GameHost;

public interface IAurelianCadenceApplication : IDisposable
{
    SimulationExecutionRate ExecutionRate { get; }
    void OnResize(HostSurfaceSize size);
    void OnCadenceAdvance(AurelianHostFrame frame, CadenceAdvanceResult advance);
    void OnRender(AurelianHostFrame frame);
}

/// <summary>
/// Adapts host time to ordered cadence facts. The wrapped application remains responsible
/// for interpreting facts and invoking its authoritative resolver.
/// </summary>
public sealed class AurelianCadenceApplication : IAurelianGameApplication
{
    private readonly CadenceScheduler scheduler;
    private readonly IAurelianCadenceApplication application;

    public AurelianCadenceApplication(
        CadenceScheduler scheduler,
        IAurelianCadenceApplication application)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public CadenceAdvanceResult? LastAdvance { get; private set; }

    public void OnResize(HostSurfaceSize size)
    {
        application.OnResize(size);
    }

    public void OnSimulationTick(AurelianHostFrame frame)
    {
        CadenceAdvanceResult advance = scheduler.Advance(frame.Elapsed, application.ExecutionRate);
        LastAdvance = advance;
        application.OnCadenceAdvance(frame, advance);
    }

    public void OnRender(AurelianHostFrame frame)
    {
        application.OnRender(frame);
    }

    public void Dispose()
    {
        application.Dispose();
    }
}
