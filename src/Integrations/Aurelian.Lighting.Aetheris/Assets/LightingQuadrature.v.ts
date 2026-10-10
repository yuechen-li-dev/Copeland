// GPL-3.0-only. Sixteen authored cosine-weighted hemisphere samples, normal +Y.
export function HemisphereDirection(index: u32): float3 {
    if (index == 0) {
        return float3(0.176776695, 0.984250984, 0.000000000);
    }
    if (index == 1) {
        return float3(-0.225772188, 0.951971638, 0.206825818);
    }
    if (index == 2) {
        return float3(0.034558052, 0.918558654, -0.393771179);
    }
    if (index == 3) {
        return float3(0.284571220, 0.883883476, 0.371172764);
    }
    if (index == 4) {
        return float3(-0.522223187, 0.847791248, -0.092373929);
    }
    if (index == 5) {
        return float3(0.494695392, 0.810092587, -0.314684715);
    }
    if (index == 6) {
        return float3(-0.165465927, 0.770551750, 0.615525001);
    }
    if (index == 7) {
        return float3(-0.315561468, 0.728868987, -0.607594404);
    }
    if (index == 8) {
        return float3(0.684642162, 0.684653197, 0.250030219);
    }
    if (index == 9) {
        return float3(-0.712256086, 0.637377439, 0.294008958);
    }
    if (index == 10) {
        return float3(0.343354499, 0.586301970, -0.733728620);
    }
    if (index == 11) {
        return float3(0.253730241, 0.530330086, 0.808931990);
    }
    if (index == 12) {
        return float3(-0.764745892, 0.467707173, -0.443185876);
    }
    if (index == 13) {
        return float3(0.897133984, 0.395284708, -0.197232390);
    }
    if (index == 14) {
        return float3(-0.547506905, 0.306186218, 0.778772232);
    }
    if (index == 15) {
        return float3(-0.126486773, 0.176776695, -0.976089697);
    }
    return float3(0.0, 1.0, 0.0);
}
