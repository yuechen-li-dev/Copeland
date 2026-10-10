import { TraceField } from "./FieldTrace3D";
import { SurfaceRadiance, TraceSurfaceField } from "./FieldSurface3D";
import { Add3, Scale3, Unit, Sub3, Dot3 } from "./Lighting3D";
import { HemisphereDirection } from "./LightingQuadrature";
import { TraceBudget } from "./FieldTraceBudget";

@space(clip.position)
type ClipPosition4 = float4;
@material
@binding(0)
record BakeMaterial {
    origin: float4;
    axisU: float4;
    axisV: float4;
    parameters: float4;
    light: float4;
    eye: float4;
}
stream BakeResources {
    @binding(0) bake: BakeMaterial;
}
stream BakeInput {
    @location(0) position: float2;
}
stream BakeVaryings {
    @builtin(position) position: ClipPosition4;
    @location(0) uv: float2;
}
stream BakeOutput {
    @target(0) color: float4;
}
@vertex
function VertexMain(input: BakeInput): BakeVaryings {
    return {
        position: float4(input.position.x, input.position.y, 0.0, 1.0),
        uv: float2(input.position.x * 0.5 + 0.5, input.position.y * 0.5 + 0.5)
    };
}
@pixel
function PixelMain(input: BakeVaryings, resources: BakeResources): BakeOutput {
    const settings: float4 = resources.bake.parameters;
    const light: float3 = float3(resources.bake.light.x, resources.bake.light.y, resources.bake.light.z);
    const p: float3 = float3(
        resources.bake.origin.x + input.uv.x * resources.bake.axisU.x + input.uv.y * resources.bake.axisV.x,
        resources.bake.origin.y + input.uv.x * resources.bake.axisU.y + input.uv.y * resources.bake.axisV.y,
        resources.bake.origin.z + input.uv.x * resources.bake.axisU.z + input.uv.y * resources.bake.axisV.z);
    // This first admitted receiver domain is a horizontal plane, normal +Y.
    const origin: float3 = Add3(p, float3(0.0, 0.004, 0.0));
    if (settings.x < 0.5) {
        const shadow: float4 = TraceField(origin, light, 48.0, TraceBudget());
        var value: f32 = 0.0;
        var resolved: f32 = 1.0;
        if (shadow.x == 0.0) {
            resolved = 0.0;
        }
        if (shadow.x == 2.0) {
            value = 1.0;
        }
        return { color: float4(value, value, value, resolved) };
    }
    if (settings.x > 1.5) {
        const eye: float3 = float3(resources.bake.eye.x, resources.bake.eye.y, resources.bake.eye.z);
        const view: float3 = Unit(Sub3(eye, p));
        const direction: float3 = float3(-view.x, view.y, -view.z);
        const trace: float4 = TraceSurfaceField(origin, direction, 48.0, TraceBudget());
        if (trace.x == 1.0) {
            return { color: SurfaceRadiance(Add3(origin, Scale3(direction, trace.y)), light, TraceBudget()) };
        }
        if (trace.x == 0.0) {
            return { color: float4(0.0, 0.0, 0.0, 0.0) };
        }
        return { color: float4(resources.bake.light.w, resources.bake.light.w, resources.bake.light.w, 1.0) };
    }
    var radiance: float3 = float3(0.0, 0.0, 0.0);
    var resolved: f32 = 1.0;
    // Explicit finite cosine-weighted quadrature: a bounded one-bounce experiment, not converged GI.
    for (var index: u32 = 0; index < 16; index = index + 1) {
        const direction: float3 = HemisphereDirection(index);
        const trace: float4 = TraceSurfaceField(origin, direction, 48.0, TraceBudget());
        if (trace.x == 0.0) {
            resolved = 0.0;
        }
        if (trace.x == 1.0) {
            const hit: float4 = SurfaceRadiance(Add3(origin, Scale3(direction, trace.y)), light, TraceBudget());
            radiance = Add3(radiance, float3(hit.x, hit.y, hit.z));
            resolved = Min(resolved, hit.w);
        }
    }
    // Unit-albedo Lambertian receiver: cosine-weighted average gives outgoing diffuse radiance.
    const average: float3 = Scale3(radiance, 1.0 / 16.0);
    return { color: float4(average.x, average.y, average.z, resolved) };
}
