# Runtime arrays

Copeland arrays use familiar TypeScript shapes with an explicit mutation boundary.

```ts
const values: int[] = [10, 20, 30];
const middle: int = values[1];
const count: int = values.length;
```

`T[]` is immutable from Copeland source. It is dense and homogeneous; indexed
reads are checked and never return `undefined`.

## Repeated initializers

Postfix `value ... N` produces exactly `N` elements:

```ts
const zeros: int[] = [0 ... 6];
const mixed: int[] = [1 ... 2, 2 ... 3, 9];
const nested: int[][] = [[7 ... 3] ... 2];
```

Counts are nonnegative compile-time `int` arithmetic or closed static parameters.
Zero produces no elements and performs no value evaluation, while still checking
the value's type and legality. Each occurrence evaluates the value independently,
including function calls, nested constructors, failures, and awaits. Repeating an
existing reference follows ordinary aliasing rules; it does not clone that value.
Initializers using repetition admit at most 65,536 logical elements per literal.

A final `value ...` fills an authored table column or table `with` replacement
whose extent is fixed. For ordinary `T[]`, write a count: the type carries no
length. This is initializer repetition; prefix spread, rest parameters, runtime
counts, and dependent table template initialization remain unsupported.
See [the implementation qualification](../reviews/tablescript-ellipsis-implementation.md)
for table examples, diagnostics, and limits.

Use `MutableArray<T>` for fixed-length computational storage:

```ts
function squares(count: int): int[] {
    const buffer: MutableArray<int> = MutableArray<int>(count);
    let index: int = 0;
    while (index < buffer.length) {
        buffer[index] = index * index;
        index = index + 1;
    }
    return buffer.freeze();
}
```

`freeze()` copies the current contents into an immutable array. The mutable buffer
has no sparse-array or JavaScript prototype operations. Both mutable and immutable
arrays support `for...of`.

M0 fixed-length mutable storage supports Copeland's existing `int`,
`float`/`number`, and `boolean` types, which have deterministic null-less default
values. Strings, records, and enums require a future initializer-taking
constructor rather than hidden host defaults. Narrow numeric and byte types are
not yet part of the language and are not simulated.
