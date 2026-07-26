import { Package, Workspace, define } from "tspack/manifest";
export default define(<Workspace name="sample"><Package name="a" version="1.0.0" kind="app"><Package name="nested" version="1.0.0" kind="app" /></Package><Package name="a" version="1.0.0" kind="app" /></Workspace>);
