# Optional values in Copeland TS

Copeland has no ambient `null` or `undefined`. Optional values use an explicit
two-case value type:

```text
Traditional TypeScript: string | null | undefined
Copeland:               Option<string>
```

The surface remains familiar:

```ts
record User {
    name: string;
    nickname?: string;
}

const label: string = user.nickname ?? "Anonymous";
const length: int = user.nickname?.length ?? 0;
```

`nickname?: string` means `nickname: Option<string>`. Every `User` has that
field; the value is `None` or `Some(string)`. Omitting the field in a record
literal supplies `None`, and supplying a plain string lifts it to `Some`.

The explicit API is:

```ts
const present: Option<string> = Some("Ada");
const absent: Option<string> = None;

const value: string = match present {
    Some(name) => name,
    None => "fallback",
};
```

`??` evaluates its left side once and evaluates its fallback only for `None`.
`Some(false)`, `Some(0)`, and `Some("")` are present and never select the
fallback.

`?.` evaluates its receiver once. It projects a member or call through `Some`
and returns `None` for `None`. For familiar chains, it flattens one layer when
the projected member/call already returns Option:

```ts
user.address?.city ?? "Unknown"
```

This flattening belongs only to `?.`. `Option<Option<T>>` otherwise remains
nested, and `??` unwraps exactly one layer.

Postfix `?` is different: it remains Copeland Result/error propagation. Option
uses `match`, `?.`, and `??`; it does not propagate absence with `?`.

