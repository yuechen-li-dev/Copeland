import { Sub3, Add3, Scale3, Dot3 } from "./Lighting3D";

// Integrate exponential height extinction along the complete eye-to-surface segment.
export function Fogged(color: float3, point: float3, eye: float3, parameters: float4, fog: float4): float3 {
    if (parameters.x <= 0.0) {
        return color;
    }
    let delta: float3 = Sub3(point, eye);
    let distance: f32 = Sqrt(Max(Dot3(delta, delta), 0.000001));
    let end: f32 = Min(distance, fog.w);
    let length: f32 = Max(end - parameters.w, 0.0);
    let startHeight: f32 = eye.y + delta.y * parameters.w / distance;
    let heightDelta: f32 = delta.y * length / distance;
    let exponent: f32 = Clamp(parameters.y * heightDelta, -40.0, 40.0);
    var integral: f32 = 1.0 - exponent * 0.5 + exponent * exponent / 6.0;
    if (Abs(exponent) > 0.001) {
        integral = (1.0 - Exp(-exponent)) / exponent;
    }
    let density: f32 = parameters.x * Exp(Clamp(-parameters.y * (startHeight - parameters.z), -40.0, 40.0));
    let transmission: f32 = Exp(-Min(density * length * integral, 80.0));
    return Add3(Scale3(color, transmission), Scale3(float3(fog.x, fog.y, fog.z), 1.0 - transmission));
}
