# CTS-TABLE-QUERY-M1 — Typed table querying

Record tables remain immutable columnar values. `Scores.rows()` is a typed,
non-owning row-view sequence: each logical row carries only its table identity
and current index, and fields read the corresponding source columns.

```ts
function highScores(scores: Scores): ScoreView[] {
    return scores.rows()
        .where(row => row.score >= 90.0)
        .select(scoreView);
}

for (const row of Scores.rows()) {
    // row is Scores.Row; row fields are immutable.
}
```

`where` preserves declaration-order positions and `select` materializes only
the projected ordinary values. C# and JavaScript lower both forms to indexed
loops; table columns are neither copied nor converted to stored row objects.

Numeric columns provide `sum()`, `count()`, `average()`, `min()`, and `max()`.
`sum(empty)` is zero and `count(empty)` is zero. `average`, `min`, and `max`
on a statically empty authored table are rejected with `COPE-TABLE-0030`; this
prevents NaN, sentinels, and hidden runtime failures while a later fallibility
surface is designed.

The workbook proof uses `Employees.rows()` and an exhaustive `match` over
`Department`, then performs a bounded linear ID lookup from `Employees.id` to
`Scores.employeeId`. It does not depend on row position. Its engineering
average is 93.0.

This is typed compiled table querying, not SQL, a dataframe runtime, or a
query optimizer. General joins, indexes, grouping, dynamic query parsing, and
CLI query execution remain deliberately out of scope for this implementation.
