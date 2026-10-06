# Copeland graphics utility expressions

The graphics GPU profile accepts bounded one-shot ranked choice:

```typescript
function Choose(a: u32, b: u32): f32 {
    return when utility {
        case 0.5 when true score a
        case 0.25 when true score b
        else 0.0
    };
}
```

This reuses Oct's standalone `when utility` form. Each guard must be `bool`; every score must be `u32`; all values and the required fallback must have the same admitted GPU type. There must be 1–16 cases. Conditions execute once in source order, ineligible scores are skipped, the greatest score wins, and ties keep the earlier source case. Only the winning value executes. Zero is a valid score. Scores use ordinary unsigned shader arithmetic; authors should keep them within their intended bounded range.

The frontend normalizes each decision into a closed helper containing ordinary VD-MIR locals, branches, assignments and returns. Direct WGSL and the existing HLSL backend therefore need no special policy runtime or MIR instruction. Nested utility expressions are admitted. Host use is refused explicitly with `COPE-UTILITY-PROFILE-0001`; compute utility, enum-targeted choices, signed/float scores, commitment, hysteresis and dynamic candidate registration are not admitted by this slice.

The direct WGSL result exposes `FunctionNames`, a compiler-owned source/emitted helper symbol map for static shader composition. The map does not grant arbitrary resource/interface or stage compatibility: a composition must still satisfy ordinary WGSL bindings, types and stage validation.

Tests: `GpuUtilityTests` in `Copeland.TS.Tests`, and native `VdMirUtilityTests` in `Aurelian.Shaders.Tests`. The Three Telos temporal experiment authors its actual policy in VTS and uses this path. The frontend support does not imply visual acceptance of that experiment.
