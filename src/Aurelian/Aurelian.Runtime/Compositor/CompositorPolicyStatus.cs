namespace Aurelian.Runtime.Compositor;

public enum CompositorPolicyStatus
{
    Dispatched,
    WaitingForOutputs,
    Rejected,
    Failed,
}
