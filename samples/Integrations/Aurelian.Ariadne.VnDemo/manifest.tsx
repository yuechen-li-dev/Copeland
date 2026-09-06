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
