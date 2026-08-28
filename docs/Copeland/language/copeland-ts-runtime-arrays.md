# Runtime arrays

Copeland arrays use familiar TypeScript shapes with an explicit mutation boundary.

```ts
const values: int[] = [10, 20, 30];
const middle: int = values[1];
const count: int = values.length;
```

`T[]` is immutable from Copeland source. It is dense and homogeneous; indexed
reads are checked and never return `undefined`.

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
