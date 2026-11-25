Console.WriteLine(CopelandHello.Copeland.CopelandProject.dotNetGreeting("Copeland"));

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
Console.WriteLine(CopelandHello.Copeland.CopelandProject.quoteTitle(npmGreeting.Value.Value));
