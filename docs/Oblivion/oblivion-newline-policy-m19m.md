# Oblivion newline policy — M19m

## Existing Page behavior

The stack mutation path no longer regenerates an existing Page through the platform-default TOML writer. It reads the validated Page document for typed Card order, replaces only the existing `cards` assignment in the original text, and retains all other bytes and line terminators.

With `newline = preserve`, LF stays LF, CRLF stays CRLF, trailing-newline presence/absence is retained, and even a pre-existing mixed-line-ending Page is not normalized. Push followed by pop restores the original Page bytes exactly. Tests cover LF and CRLF with and without a trailing newline plus a mixed-line-ending regression case; real dogfood compared SHA-256 before and after and produced the same hash.

## Explicit policy

`newline = lf` normalizes only metadata files that a mutation creates or rewrites to LF. `newline = crlf` does the same with CRLF. Changing the config does not scan or rewrite the vault, and untouched files are never normalized.

Push owns the new Card TOML and the changed Page TOML, so policy applies to both. Imported Markdown is copied exactly from the supplied source and is not newline-normalized: the import transaction owns the destination file but preserves the user's authored payload bytes. Pop applies policy only to its rewritten Page metadata.

`preserve` is the default. For a new Card TOML with no prior convention to preserve, it uses the current platform convention; the existing Page retains its exact convention.
