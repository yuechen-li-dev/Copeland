// SPDX-License-Identifier: AGPL-3.0-only
// Adapted from Aetheris fixtures/three-telos/temporal-policy.v.ts.
// Weights tuned for surface motion and coverage validation in Aurelian.
// Finite deterministic policy. Integer score buckets preserve Oct's no-NaN score law.
// Result: policy (0 reject, 1 stable, 2 clamped, 3 edge), history weight, confidence.
function Score(confidence: f32): u32 {
    if (confidence >= 0.9) { return 100; }
    if (confidence >= 0.7) { return 80; }
    if (confidence >= 0.5) { return 60; }
    if (confidence >= 0.3) { return 40; }
    if (confidence >= 0.1) { return 20; }
    return 0;
}
function RejectHistory(): float3 { return float3(0.0, 0.0, 0.0); }
function StableHistory(confidence: f32): float3 { return float3(1.0, 0.875, confidence); }
function ClampedHistory(confidence: f32): float3 { return float3(2.0, 0.25, confidence); }
function EdgeConservative(confidence: f32): float3 { return float3(3.0, 0.875, confidence); }
export function TemporalPolicy(depth: f32, motion: f32, color: f32, edge: f32): float3 {
    const stable: f32 = depth * motion * color * (1.0 - edge);
    const clamped: f32 = depth * motion * (1.0 - color) * (1.0 - edge);
    const risk: f32 = Max(1.0 - depth, Max(1.0 - motion, edge * (1.0 - color)));
    return when utility {
        case RejectHistory() when true score Score(risk)
        case StableHistory(stable) when depth > 0.8 && motion > 0.7 && edge < 0.2 score Score(stable)
        case ClampedHistory(clamped) when depth > 0.8 && motion > 0.5 && edge < 0.2 score Score(clamped)
        case EdgeConservative(depth * motion * color) when depth > 0.8 && motion > 0.8 && color > 0.8 && edge >= 0.2 score Score(depth * motion * color)
        else RejectHistory()
    };
}
