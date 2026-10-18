# Start here

This repository contains three cooperating projects. They share infrastructure,
but each owns a different part of an application.

| Project | What you write | What it owns |
| --- | --- | --- |
| Copeland | Copeland TS in `.ts`, `.tsx`, or `.tsxtest` files | Language rules, static checking, lowering, generated C#/JavaScript and compiler tools |
| Machina.UI | C# UI documents, layout and input/presentation composition | UI meaning, text/layout, state and renderer-neutral presentation contracts |
| Aurelian | C# application/game definitions and explicit runtime adapters | Execution, worlds, simulation, assets, graphics/audio/inference services |

Copeland TS is a language frontend, not an alternative spelling accepted by
ordinary `tsc`. Its `Legacy` profile selects traditional TypeScript. Its other
profiles explicitly select compiler ownership/targets. Generated C# can join an
ordinary .NET project; generated JavaScript can join a declared browser host.

Machina.UI's C# API is a separate authoring route. Using it does not require
writing Copeland TS. Aurelian consumes renderer-neutral presentation through
integration adapters; the UI document is not a Vulkan renderer or a game world.

## Choose your first working path

1. **Copeland TS:** follow [Start authoring](Copeland/authoring/README.md), then
   the language guide's first SDK example. Use the [current sample catalog](../samples/copeland-ts/README.md)
   when exploring browser state/events, layouts, framework components or tables.
   Read [file profiles](Copeland/language/copeland-ts-file-profiles.md) before
   mixing Copeland and traditional TypeScript in one workspace.
2. **C# UI:** follow the [Machina.UI authoring guide](Machina.UI/authoring-guide.md)
   from document construction to layout/presentation and host input. That guide
   distinguishes a document from the integration host that displays it.
3. **C# game/runtime:** begin with the [game starter](Aurelian/game-starter.md),
   then the [game shell](Aurelian/game-shell.md). Choose a [runtime execution mode](Aurelian/runtime-execution-modes.md)
   before selecting a native window, headless scenario or finite proof runner.
4. **Existing product:** use [Games](../Games/README.md) for runnable games or
   [Oblivion](Oblivion/README.md) for the technical-work application. Start from
   its named launcher/README, not an old artifact-producing proof entry point.

Each path names prerequisites and has a concrete example. Browser interop,
native graphics and model inference have different qualification boundaries;
a successful .NET build does not establish that all three work on your machine.
The runtime index distinguishes current contracts from model-specific evidence.

## Understand an application across the layers

Keep domain truth in application code: records/tables, typed commands, explicit
validation and deterministic calculations. A frontend owns drafts and accepted
presentation state. A React/Vue control can forward input through its declared
contract; it does not become the owner of inventory or purchasing rules. A server
revalidates a request against its own inputs before committing authoritative data.

For a concrete example, the [current TableScript café](../samples/copeland-ts/tablescript-rows-m15/README.md)
reuses a workbook, application policy, typed editing logic, a small React adapter
and browser qualification tooling. Read its source map in the README. You do not
need the sequence of milestone reports to understand the running application.

When changing shared behavior, consult [architecture](architecture/README.md).
The producer owns its meaning; adapters translate it explicitly. Existing
ownership violations are a backlog, not a template to copy into a new feature.

## Find answers without archaeology

- A guide teaches an authoring workflow; follow it in order.
- A reference defines exact syntax, types, diagnostics or protocol details.
- A status page states the supported subset and limitations.
- A sample demonstrates a real executable path, with its prerequisites.
- A historical report records a particular decision or run; it may be superseded.

Start with the [documentation catalog](INDEX.md). If current prose is missing a
rule needed for a normal authoring task, repair that guide/reference and verify
it against the owning tests. Parser implementation is maintainer evidence, not
an acceptable substitute for author documentation.
