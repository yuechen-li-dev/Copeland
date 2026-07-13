# Machina presentation-only pipeline — JTF-M3d

JTF-M3d makes the renderer boundary physical. `Machina.Pipeline` now exposes `MachinaPresentationPipeline.Prepare` and returns `MachinaPreparedPresentation`: lowering, layout document, resolved layout, hit-test index, and immutable `MachinaPresentationFrame`. It does not select a backend or contain pixels, surfaces, PPM data, Aurelian plans, or Dominatus commands.

```text
Machina UI -> MachinaPresentationPipeline -> MachinaPresentationFrame
           -> Aurelian.Machina -> Resolved2DPlan
           -> AurelianCpuRasterRenderer -> RasterFrame
```

`MachinaRasterPipeline`, `MachinaRasterPipelineOptions`, and the raster-bearing `MachinaFrame` were removed. This is an intentional source compatibility break; callers that only need UI semantics use `Prepare`, while callers requiring pixels compose the bridge and Aurelian renderer outside Machina production assemblies.

`Machina.Dominatus` retains only its non-rendering runtime proof pending JTF-M5. Its renderer command vocabulary, bridges, snapshot recorder, and render actuation path have been deleted.
