# Typed UI identity and action messages

## Reusable contracts

Machina owns the identity and message primitives. Games own their domain keys,
message records, authored roles, and reducers.

- `NodeId.Child(segment)` composes a stable child ID. Each segment is escaped,
  including dots, slashes, colons and literal percent escapes. A key containing
  `a.b` cannot collide with a hierarchy containing the two segments `a` and `b`.
  Numeric children use invariant formatting. `IsWithin(scope)` checks actual
  segment boundaries.
- `UiIdFamily<TKey>` creates IDs from a namespace and a typed domain key. Its
  formatter supplies a stable domain value; it must not use display text or layout
  coordinates. Recipe and product key types cannot be interchanged.
- `UiAction<TMessage>` carries a message through the existing `UiAction` surface.
  `UiAction.Typed(id, message)` constructs it; `TryGetMessage<TMessage>` retrieves
  it at ingress. Records and other immutable values should be used for messages.
- `UiActionFamily<TKey, TMessage>` constructs parameterized messages and their
  diagnostic aliases together. The alias identifies a rendered action for tools;
  reducers consume the message and never parse the alias.
- `UiStateKey<T>` declares a widget slot's identity, value type and lifetime once.
  `UiStateScope.GetOrCreate(key, factory)` infers the value type from that key.
  Existing runtime collision checks also protect against separately declared keys
  reusing one ID with different types or lifetimes.

For example, an application can declare its own `RecipeId` and `ChooseRecipe`
message, then compose a button without concatenating either kind of ID:

```csharp
var ids = new UiIdFamily<RecipeId>("recipes", recipe => recipe.Value);
var choose = new UiActionFamily<RecipeId, MenuMessage>(
    new UiActionId("recipe"),
    recipe => recipe.Value,
    recipe => new ChooseRecipe(recipe));

UiNode button = StandardUI.Button("Choose", choose[recipeId], 20, 40, 160,
    id: ids[recipeId]);
```

The positioned `Button(text, action, ...)` overload derives a stable node ID from
the action ID when no explicit node ID is supplied. Give repeated placements of
one action distinct explicit node IDs. `UiTableRow.Id` is now `NodeId`; tables
compose their cell IDs from row identity and column, and reject duplicate row IDs.
Row IDs therefore survive reordering. Legacy string arguments still convert to
`NodeId`, and `UiAction.Named` remains available to existing name-based consumers.

## Message equality and preparation

Action equality includes the message type and value. Two actions with the same
alias but different recipes are different actions. The declaration cache notices
that difference, updates preparation, and `UiPointerActivation` rejects a release
whose current target carries a different message from the original press.

`MachinaPreparedPresentation.TryGetActionCenter(action, out point)` locates a
specific typed action. Its `UiActionId` overload is for external diagnostics:
repeated table cells carrying the same message are allowed, while one alias
associated with distinct messages is rejected as ambiguous.

## TinyFarm migration

`TinyFarmMenuMessage` is the game-owned message union. It carries recipe IDs,
product IDs, inventory identities, categories, sorts, stats groups, container
sides and paging directions directly. `TinyFarmUiActions` supplies its factories
and stable playtest aliases. Keyboard navigation and prepared pointer targets
dispatch the same messages. `DispatchMenu` accepts only
`UiAction<TinyFarmMenuMessage>` and checks the active screen before reducing it.
`TryDispatchMenu` is the checked ingress for the hit-test surface's base `UiAction`.
Name-only or foreign message actions do not enter that reducer.

`TinyFarmInventoryKey.Item` and `.Product` replace formatted row keys. An item and
a product with the same textual value remain different identities. Selection,
sorting tie breaks, transfer lookup and table row identity use those values.
No Machina dependency was added to the game core for this domain type.

`TinyFarmUiIds` supplies enum-indexed authored node IDs and typed families for
inventory rows, ingredients, choices, objectives, and read-only display slots.
`TinyFarmUiStateKeys` binds the game-specific widget-state types to their slots.
The renderer uses the same typed IDs for dialogue-layer classification.

The shell playtest API still takes a textual action alias. `TinyFarmApp.ActionPoint`
looks that alias up among the current prepared actions and returns its location.
The subsequent real pointer click delivers the original typed message. It does
not reconstruct a recipe, product, category or command from text. Existing aliases
such as `row:item:sword` and `add:turnip` continue to locate their rendered targets;
keys containing reserved characters use canonical escaped aliases.

## Evidence

Core tests cover message equality, base-type extraction, canonical escaping,
segment collisions and culture-independent numeric IDs. Pipeline tests exercise
typed table actions through lowering, hit testing and pointer activation, payload
changes with identical aliases, ambiguous diagnostic lookup, and row reordering.
TinyFarm tests click real recipe/ingredient buttons through the crafting resolver,
select/use inventory rows, and verify that dispatch follows message content even
when its diagnostic alias is renamed.

A negative compilation probe under `artifacts/local/machina-typed/compile-probe`
produces `CS1503` for a recipe ID supplied to a product action family, a string
or foreign message supplied to TinyFarm's dispatcher, and a state factory with
the wrong key value type. Its output is
`artifacts/machina-typed-negative-compilation.log`.

Validation uses the complete Machina.UI and TinyFarm builds, their full unit-test
lanes, the shared menu integration tests, and native TinyFarm playtests. This
change keeps typed messages transient; it does not introduce a message serializer
or change simulation saves and replay authority.
