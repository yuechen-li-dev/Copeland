# Copeland asset manifests in TSX

`manifest.tsx` is the authored structural composition for Copeland packages and assets. TSX is used only where nesting makes ownership legible; semantics remain in the compiler-owned manifest binder.

```tsx
import { define } from "tspack/manifest";

export default define(
    <Workspace name="sunkill" runtime="nodejs">
        <Assets root="Assets">
            <Texture id="sunkill.ui.atlas" src="sunkill-ui-atlas.png" />
            <Object
                id="sunkill.dialogue-panel"
                src="sunkill-dialogue-panel.obj.ts"
                dependsOn={[]}
            />
        </Assets>
        <AssetOutputs>
            <Toml />
            <Json />
            <Runtime />
            <Audit />
        </AssetOutputs>
    </Workspace>,
);
```

Manifest v1 adds only `Assets`, `Texture`, `Object`, and `AssetOutputs` to the existing restricted workspace vocabulary. `root`, stable IDs, source paths, and object dependencies lower to immutable `ManifestAssetGraph`; output elements lower to `ManifestAssetOutputs`.

`tscl asset build path/to/manifest.tsx` loads the root manifest, rejects unsafe/missing files, duplicate IDs, wrong object suffixes, unknown dependencies, cycles, unknown outputs, and duplicate output requests. It topologically compiles objects and verifies that every object texture is registered.

The generated `manifest.generated.json` is deterministic and contains the package name, source root, sorted textures and objects, dependencies, semantic IDs, panel IDs, requested outputs, and emitted file list. It is an interoperability/evidence projection, not authoring authority. JSON-first manifests remain compatible in their existing consumers; M15 does not purge JSON or TOML.

The authoring law is simple: edit `manifest.tsx` and `*.obj.ts`, run the asset build, and do not hand-edit generated TOML, JSON, audit, or runtime metadata.
