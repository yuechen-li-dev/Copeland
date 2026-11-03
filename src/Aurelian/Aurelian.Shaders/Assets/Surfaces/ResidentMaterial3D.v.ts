import { SurfaceMaterial } from "./SurfaceMaterial3D";

export record BindlessMaterialRecord {
    surface: SurfaceMaterial;
    baseTexture: u32;
    metalTexture: u32;
    normalTexture: u32;
    occlusionTexture: u32;
    emissiveTexture: u32;
    baseSampler: u32;
    metalSampler: u32;
    normalSampler: u32;
    occlusionSampler: u32;
    emissiveSampler: u32;
    reserved0: u32;
    reserved1: u32;
}
