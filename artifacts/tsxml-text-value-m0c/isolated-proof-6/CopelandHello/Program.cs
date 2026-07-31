using Copeland.TS.Backend.CSharp;

Console.WriteLine(CopelandHello.Copeland.CopelandProject.dotNetGreeting("Copeland"));

await using CSharpSidecarHost npm = CSharpSidecarHost.AttachNode(
    typeof(CopelandHello.Copeland.CopelandProject).Assembly,
    AppContext.BaseDirectory,
    "scripts/npm-sidecar.mjs");
var npmGreeting = CopelandHello.Copeland.CopelandProject.npmGreeting("HELLO from npm");
var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
if (!npmGreeting.SubscribeTransport(
        () => completed.TrySetResult(),
        () => completed.TrySetException(new InvalidOperationException("npm call was cancelled")),
        () => completed.TrySetException(new InvalidOperationException("npm transport failed")),
        () => completed.TrySetException(new InvalidOperationException("npm call panicked"))))
{
    await completed.Task;
}

if (!npmGreeting.Value.IsOk)
{
    throw new InvalidOperationException("npm package returned an error");
}

Console.WriteLine($"lodash-es says: {npmGreeting.Value.Value}");
