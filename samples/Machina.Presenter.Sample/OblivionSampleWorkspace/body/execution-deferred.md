# Execution deferred

Code fences render as static text only in M12c.

```csharp
[Fact]
public void MarkdownCards_ComeBeforeExecution()
{
    // Deferred until M13+.
}
```

No Roslyn or xUnit execution is enabled here.
Inline `FactAttribute` references stay presentation-only, **not executable**, and [execution planning](../../../../docs/machina-support-roadmap.md) remains deferred.
