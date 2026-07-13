# Dominatus Authoring Primer for Machina

> Historical status: this predates the JTF-M3d retirement of Machina's Dominatus renderer route. Dominatus may still host coarse event-spanning UI behavior only through `src/Integrations/Machina.Dominatus`; Machina core remains independent. See [JTF-M5b Dominatus ownership consolidation](architecture/jtf-dominatus-ownership-consolidation.md).

This document is a quick guide for authors integrating future Machina rendering and runtime work with Dominatus.

## Core Dominatus concepts

- HFSM stack frames
  - Runtime state is represented as explicit pushed and popped node frames.
- Iterator-style node authoring
  - Nodes are authored as step-wise logic that yields deterministic transitions and effects.
- Blackboard
  - Shared state surface for runtime data.
- Mailbox
  - Input/event ingress channel.
- Actuation commands
  - Typed command objects describing side effects.
- Actuator host
  - Backend seam that executes actuation commands.
- OptFlow
  - Flow and dialogue orchestration patterns.
- UtilityLite
  - Lightweight utility/decision helpers for orchestrating choices.

## Required Machina mapping

- Dominatus blackboard
  - Maps to render/canvas state plus dirty/revision gating.
- Dominatus actuation command
  - Maps to draw commands or runtime side-effect commands.
- `ActuatorHost`
  - Maps to renderer/backend adapter seam.
- HFSM push/pop
  - Maps to scoped UI declaration, render scope, and canvas state stack behavior.
- Mailbox/input
  - Maps to UI action and event ingress.
- OptFlow
  - Maps to dialogue and flow patterns for future app/runtime logic.
- UtilityLite
  - Maps to lightweight utility-driven orchestration choices.

## Practical conclusion

Do not reinvent renderer dispatch, dirty tracking, push/pop scopes, or action routing under new names.

Use Dominatus deliberately where runtime/rendering needs those semantics.
