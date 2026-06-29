# Execution deferred

Code fences render as static text only in M12b.

```csharp
[Fact]
public void MarkdownCards_ComeBeforeExecution()
{
    // Deferred until M13+.
}
```

No Roslyn or xUnit execution is enabled here.
