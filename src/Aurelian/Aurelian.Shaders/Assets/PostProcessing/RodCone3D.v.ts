import { Add3, Dot3, Scale3, Sub3 } from "./Lighting3D";

function SceneLuminance(rgb: float3): f32 {
    return Dot3(rgb, float3(0.2126, 0.7152, 0.0722));
}

// RGB fit from ColorChecker reflectances under five illuminants. The fit uses
// the Stockman–Sharpe 2° fundamentals and the CIE 1951 scotopic observer.
// Rows are normalized to D65 white, with L + M = 1 and rod response = 1.
function Cones(rgb: float3): float3 {
    return float3(Dot3(rgb, float3(0.135675, 0.326340, 0.037985)),
        Dot3(rgb, float3(0.049750, 0.386393, 0.063857)),
        Dot3(rgb, float3(0.010449, 0.045160, 0.444391)));
}

// Kirk and O'Brien, SIGGRAPH 2011, equations 10–12. Receptor scaling is in
// scene-relative units. These are the per-unit-rod increments in opponent space.
function RodOpponent(cones: float3, rod: f32, scale: f32): float3 {
    let longGain: f32 = 1.0 / Sqrt(1.0 + scale * (cones.x + 0.25 * rod)) / 0.637;
    let mediumGain: f32 = 1.0 / Sqrt(1.0 + scale * (cones.y + 0.25 * rod)) / 0.392;
    let shortGain: f32 = 1.0 / Sqrt(1.0 + scale * (cones.z + 0.4 * rod)) / 1.606;
    let achromatic: f32 = 0.619 * longGain + 0.381 * mediumGain;
    return float3(3.75 * (1.111 * mediumGain - 0.939 * longGain),
        15.0 * (0.4 * shortGain - 0.15 * achromatic), 5.0 * achromatic);
}

function OpponentChroma(redGreen: f32, blueYellow: f32): float3 {
    // Invert the opponent and RGB-to-LMS transforms for the two chromatic axes.
    let rgb: float3 = float3(-9.852552 * redGreen + 0.388984 * blueYellow,
        2.567427 * redGreen - 0.427641 * blueYellow,
        -0.029252 * redGreen + 2.284579 * blueYellow);
    let luminance: f32 = SceneLuminance(rgb);
    return Sub3(rgb, float3(luminance, luminance, luminance));
}

export function ApplyRodCone(input: float3, adaptedLuminance: f32, strength: f32, photopicLuminance: f32): float3 {
    let luminance: f32 = SceneLuminance(input);
    let lightLevel: f32 = Max(luminance, adaptedLuminance);
    if (strength <= 0.0 || lightLevel >= photopicLuminance || luminance <= 0.0000001) {
        return input;
    }

    // Scene adaptation engages rods in shadows. Bright local cues stay photopic.
    let transition: f32 = Clamp((lightLevel / photopicLuminance - 0.0625) / 0.9375, 0.0, 1.0);
    let mesopic: f32 = 1.0 - transition * transition * (3.0 - 2.0 * transition);
    let rod: f32 = Dot3(input, float3(0.0, 0.533263, 0.466737));
    let receptorScale: f32 = 3.96 / photopicLuminance;
    let response: float3 = RodOpponent(Cones(input), rod, receptorScale);
    let grayCone: f32 = 0.5 * luminance;
    let neutral: float3 = RodOpponent(float3(grayCone, grayCone, grayCone), luminance, receptorScale);

    // Anchor to neutral at equal photopic luminance. Bound the change in
    // luminance to keep color-coded surfaces legible at gameplay light levels.
    let rodLuminance: f32 = Clamp(rod * response.z / neutral.z, 0.55 * luminance, 1.5 * luminance);
    let rodFraction: f32 = strength * mesopic;
    let outputLuminance: f32 = luminance + (rodLuminance - luminance) * rodFraction;
    let gray: float3 = float3(luminance, luminance, luminance);
    let relativeOpponent: float3 = OpponentChroma(response.x - neutral.x, response.y - neutral.y);
    let chroma: float3 = Add3(Scale3(Sub3(input, gray), 1.0 - rodFraction),
        Scale3(relativeOpponent, rodFraction * (1.0 - mesopic) * rod * 0.25));

    // Fit chroma around the new luminance without clipping individual channels.
    let chromaScale: f32 = Min(1.0, outputLuminance / Max(-Min(chroma.x, Min(chroma.y, chroma.z)), 0.0000001));
    return Add3(float3(outputLuminance, outputLuminance, outputLuminance), Scale3(chroma, chromaScale));
}
