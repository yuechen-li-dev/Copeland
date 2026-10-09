// Experimental receiver-space visibility. RGB contains signed distances, never sRGB color.
// Inside the projected occluder is > 0.5. The caller owns projection and depth validity.
export function DistanceFieldShadowVisibility(sample: float4): f32 {
    const distance: f32 = Max(Min(sample.x, sample.y), Min(Max(sample.x, sample.y), sample.z));
    // Same one-screen-pixel reconstruction as ProfileMsdf.v.ts.
    const width: f32 = Max(Fwidth(distance), 0.000001);
    const t: f32 = Clamp((distance - 0.5 + width * 0.5) / width, 0.0, 1.0);
    const coverage: f32 = t * t * (3.0 - 2.0 * t);
    return 1.0 - coverage;
}
