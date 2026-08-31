# M19l full-content dogfood

This note exercises the semantic full-Card read path after a real stack push.

## Read-side contract

The raw command should return this complete Markdown source with no metadata banner, preview limit, rendering, UI state, or implementation-source lookup. The JSON command should add only stable semantic identity, the vault-relative source reference, and the same full payload.

## Dogfood question

Together, `card show` and `card content` should cover bounded inspection and complete reading while `card peek` supplies the current top-of-stack identity.
