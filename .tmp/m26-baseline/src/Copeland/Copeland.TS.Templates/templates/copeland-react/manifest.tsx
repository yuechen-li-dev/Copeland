import { Env, Package, RunTargets, Workspace, define } from "tspack/manifest";

export default define(
    <Workspace name="copeland-react" runtime="nodejs">
        <Package name="copeland-react" version="0.1.0" kind="service" dependencies={{ values: [] }}>
            <RunTargets
                rows={[{
                    name: "web",
                    runtime: "system",
                    cwd: "workspace",
                    command: ["dotnet", "run", "--no-build", "--", "--urls", "http://127.0.0.1:5137"],
                    url: "http://127.0.0.1:${PORT}",
                    ready: { kind: "http", path: "/" },
                    env: [
                        Env("PORT", { default: "5137", description: "Local ASP.NET Core browser host port" }),
                        Env("COPLAND_DISABLE_BROWSER", { default: "1", description: "TSPack owns browser launch during automated runs" })
                    ]
                }]}
            />
        </Package>
    </Workspace>
);
