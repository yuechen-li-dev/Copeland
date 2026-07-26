import { Package, RunTargets, definePackage } from "tspack/manifest";
export default definePackage(<Package name="dependency" version="1.0.0" kind="service"><RunTargets rows={[{ name: "launch", command: ["server.js"] }]} /></Package>);
