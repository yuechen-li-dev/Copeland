# Copeland M0 templates

The local-feed template package supplies a deliberately small catalog:

| Template | Command | Dependency law |
| --- | --- | --- |
| Console | `dotnet new copeland-console -n Example` | CLR only; no TSPack. |
| Library | `dotnet new copeland-library -n Example` | CLR only; no TSPack. |
| React web app | `dotnet new copeland-react -n Example` | TSPack-supervised ASP.NET Core/browser lifecycle; no npm materialization in M0. |
| Mixed workspace | `dotnet new copeland-workspace -n Example` | Run `tscl workspace sync`; conventional TypeScript remains tsc-owned. |

`copeland-react` intentionally uses the smallest browser experience: an
ASP.NET Core host, a React reducer, and a Copeland-compiled API. Its
`tsconfig.tsx` is the Copeland ownership map and its `manifest.tsx` declares the
TSPack `web` RunTarget. TSPack owns host supervision, readiness, browser
inspection, and cleanup. It does not claim to package npm dependencies; TSPack
stays separate from Copeland and will materialize npm only when the template
declares those dependencies.
