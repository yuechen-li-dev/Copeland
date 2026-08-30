# Oblivion Action and Effect Boundary M18d

## Contract layers

OblivionCardAction is the durable format-1 declaration. Its identity is now
OblivionProductActionId; the Id string property and string constructor are
explicit persistence/API compatibility adapters.

OblivionCardActionInvocation is a typed runtime value containing card identity,
product action identity, page identity, and optional source reference.

OblivionUiActions is the only compatibility codec between Machina UiActionId
strings and typed OblivionInteraction records. Presenter forwards that generic
UI action identity; it neither parses nor interprets product semantics.

## Validation and transition

OblivionInteractionDispatcher validates page/card targets, resolves legacy
source-name aliases, applies selection/expansion/scroll transitions, and invokes
product actions. Unknown cards and unknown actions return deterministic
diagnostic codes instead of silently acquiring host meaning.

OblivionApplication accepts OblivionProductActionId, asks the card handler for
an evidenced effect request, routes it, and applies the request/result pair to
OblivionEffectState.

## Typed effects

The former enum-plus-property-bag request is replaced by:

- RefreshContentEffectRequest
- OpenSourceEffectRequest
- CopySourcePathEffectRequest
- OpenArtifactEffectRequest
- RunCodeFactEffectRequest
- RunCodeTheoryEffectRequest
- ExportCardEffectRequest
- RenderPreviewEffectRequest
- explicit no-op/custom compatibility variants

Shared metadata is the typed OblivionEffectContext, not a string dictionary.

Results are typed as DeferredEffectResult, RejectedEffectResult, or
CompletedEffectResult. OblivionEffectState.WithOutcome rejects a result whose
request ID, card ID, or effect kind does not match its request.

## Host capabilities

OblivionHostCapabilities is a small record of delegates for the six currently
evidenced platform-capable operations: refresh content, open source, copy source
path, open artifact, export card, and render preview. No service provider,
command bus, dispatcher framework, or registration mechanism exists.

The default host supplies no capabilities, preserving the established deferred
behavior. A missing capability emits
OBLIVION-HOST-CAPABILITY-UNAVAILABLE. Code-fact and code-theory execution stay
explicitly deferred product work and are not exposed as host capabilities.

## Effect flow

~~~text
Machina UiActionId
  -> OblivionInteraction
  -> OblivionCardActionInvocation
  -> typed OblivionEffectRequest
  -> explicit OblivionHostCapabilities delegate, when present
  -> typed OblivionEffectResult
  -> validated OblivionApplicationState transition
~~~

## Compatibility and non-goals

Format-1 action IDs remain strings on disk and round-trip unchanged. The UI
codec preserves a string boundary only because Machina actions are durable UI
identities; typed product meaning exists immediately on the Oblivion side.

M18d adds no execution, networking, editing, agent, DI, repository, or generic
effect framework.

