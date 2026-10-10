using Xunit;

namespace Aurelian.Physics3D.Bepu.Tests;

internal static class PhysicsTestThreads
{
    internal static Exception? RunOffOwnerThread(Action action)
    {
        Exception? failure = null;
        // Task.Run followed by a synchronous wait can execute on the waiting worker.
        // A dedicated thread actually tests the backend's thread ownership boundary.
        var thread = new Thread(() => failure = Record.Exception(action));
        thread.Start();
        thread.Join();
        return failure;
    }
}
