# Copeland GPU utility expressions

The compute and graphics GPU profiles accept bounded one-shot ranked choice.

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

Both frontends normalize the decision into a helper with VD-MIR locals, branches, assignments and returns. Graphics emits direct WGSL or HLSL. Compute emits HLSL and validated SPIR-V. In compute, the helper captures the local values and storage buffers it reads. Entry workgroup arrays retain their shared storage, and the compute entry still owns synchronization.

Nested expressions use the same bounded selection rules. Host use reports `COPE-UTILITY-PROFILE-0001`. The admitted form uses `u32` scores and a fixed candidate list. E2B's compute argmax maps eligible FP32 logits to ordered `u32` keys for local selection and performs the cross-lane reduction separately. Equal logits keep the smaller token ID, including when positive and negative zero compare equal.

The direct WGSL result exposes `FunctionNames`, a compiler-owned source/emitted helper symbol map for static shader composition. The map does not grant arbitrary resource/interface or stage compatibility: a composition must still satisfy ordinary WGSL bindings, types and stage validation.

`GpuUtilityTests` covers both binders. `VdMirUtilityTests` validates compute
SPIR-V and the two graphics stages. The Gemma proof exercises compute
selections across multiple workgroups on Vulkan.

## Proposed Visual TypeScript spelling

A contextual `decide` before a brace fits this expression. The case-level
`score` still distinguishes ranked choice from a pattern match. Keeping the
name contextual would preserve existing identifiers, and keeping
`when utility` as an alias would preserve Oct-style source. The compiler
currently accepts `when utility`.
