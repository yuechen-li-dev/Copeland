# CTS-COMPONENT-CAPSULE-M0 — current implementation review

## Implemented component law

Components are functions with private local presentation domains. The compiler
recognizes each ordinary `ReactNode` function as a component definition; no
class hierarchy, component-only function system, or React node identity is
introduced. A definition has a stable function-derived identity, typed
parameters, implementation kind, optional stream realization, and a small
`FillAssignedBox` host capability.

Parent stream bindings produce component instances only for normal component
function calls in named slots or bounded collections. The instance records its
parent host, definition, argument types, ordinal, and renderer adapter. The
parent keeps the authoritative outer geometry. A component never receives a
generated placement class and cannot resize a sibling through props.

`component::Definitions`, `component::Instances`, and
`component::Bindings` are read-only projected tables. They expose props as a
shape and call arguments as type/kind only; arbitrary runtime values are never
serialized. Existing `layout::Boxes` and `layout::Bindings` remain the one
layout/binding projection.

## Layout, stream, and renderer boundary

Existing `layout` and `stream` declarations remain canonical. A regular
function that returns a generated stream function is `NativeMachina`; an
ordinary TS-XML render function is `React`. This keeps component identity
function-derived while using React as the current actuator.

A future renderer adapter owns its child-root mount, update, unmount, prop
transfer, event bridge, and cleanup. It cannot own parent geometry, sibling
placement, or parent tables. Vue/custom elements must mount an isolated child
root in an assigned neutral host; arbitrary React/Vue virtual-tree mixing is
not claimed.

## Website dogfood and locality evidence

The website now contains one typed `FeatureCard(props)` implementation and one
`Hero(profile)` implementation. Desktop, Tablet, and Mobile streams retain one
outer `featureGrid` and one outer `hero` box; neither repeats card markup or
hero internals. The browser proof rebuilds and checks all three profiles,
changes hero copy, verifies stable hero/action/grid geometry, containment,
horizontal-overflow absence, scrolling to the footer, semantic document DOM,
and clean console/page/request diagnostics. It writes deterministic screenshots
to the ignored artifact directory.

The built website projection contains 11 definitions, 30 instances, and 15
bounded prop bindings. The native stream-backed path is represented by
`CopelandSite`; `FeatureCard` and `Hero` are React-actuated implementations.

## Deliberate M0 limits and next work

This increment does not yet introduce lexical component-local `layout` or
`stream` declarations that capture function props, privacy-aware component LSP
navigation, native state/hooks, portals, intrinsic parent-size negotiation, or
a Vue runtime. The existing stream path supports a normal function returning a
static stream, and the component model records that local stream, but typed
document interpolation inside a prop-capturing stream needs shared
expression/document binding first.

The next semantic question toward React-optional Copeland is how a component
function declares a prop-capturing local stream while reusing the existing
layout normalizer and document binder. It should create qualified private box
identities for tooling, reject parent source references to them, and provide
LSP navigation without adding those identities to parent completion.
