# Oblivion full Card content CLI — M19l

## Command

```text
oblivion card content <card-id> -w <vault> [--page <id>] [--json]
```

The command resolves one exact semantic Card. Structured vaults enforce workspace-global Card IDs, so omission of `--page` uses the same deterministic workspace scan as `card show`. An explicit `--page` narrows lookup to that exact durable Page. Neither path consults GUI selection or session state.

## Semantic ownership

`Oblivion.Cli` maps `System.CommandLine` arguments, selects human or JSON formatting, and maps the product result to an exit code. `OblivionWorkspaceControl.GetCardContent` owns Card/Page resolution and the typed `OblivionCardContentResult`. `OblivionApplication.OpenWorkspace` and the existing Persistence loader own vault structure, source-path safety, Markdown loading, and diagnostics.

The CLI does not open Markdown, parse TOML/JSON, resolve vault paths, or depend on UI state.

## Human raw output

Human mode writes `Content` directly to stdout with `TextWriter.Write`. It adds no heading, metadata banner, ellipsis, or terminal newline. The output is therefore the complete Markdown payload and composes with redirection and pipes:

```text
oblivion card content physical-atom -w ./M19iNotebook.oblivion > physical-atom.md
oblivion card content physical-atom -w ./M19iNotebook.oblivion | less
```

## JSON output

`--json` writes one deterministic camel-case object with stable record property order:

```json
{
  "workspaceId": "m19i-notebook",
  "pageId": "notebook",
  "cardId": "physical-atom",
  "contentKind": "markdown",
  "source": "content/physical-atom.md",
  "content": "# The physical atom of Oblivion\n...",
  "diagnostics": []
}
```

The payload contains no timestamps, presentation state, layout state, Document MIR, or renderer output.

## Source and text semantics

M19l returns the exact decoded semantic string already consumed by Oblivion: Persistence uses `File.ReadAllText`, then stores that string in `OblivionCardBody.RawText`. `card content` does not normalize it. CRLF versus LF and a trailing newline are preserved in the returned string; JSON escapes those characters and a JSON parser reconstructs the same string.

This is a text contract, not a byte-stream contract. Encoding detection and a possible byte-order mark remain properties of the existing `File.ReadAllText` load; the encoding preamble is not Card content. M19l adds no second decoding or encoding path.

## Failures

- An unknown Card reports `unknown-card`; human mode writes the diagnostic to stderr and JSON mode writes a structured failure to stdout. Both return a non-zero product exit code.
- An unknown explicit Page reports the existing `unknown-page` diagnostic.
- A missing Markdown source reports the existing `missing-markdown-body-file` Persistence diagnostic. Empty content is never substituted and no alternate file is searched.
- A resolved non-Markdown body reports `OBLIVION-CARD-CONTENT-NOT-TEXT`. M19l does not stream artifact or binary content.

No product failure prints a stack trace.

## `show` versus `content`

`card show` remains the metadata-oriented inspection command and retains its normalized 400-character preview. `card content` is the payload-oriented read command and returns the complete, unnormalized Markdown string. The separation keeps quick inspection bounded while making complete reading shell-composable.

## Non-goals

M19l adds no edit, append, rename, remove, move, reorder, Page mutation, search, artifact retrieval, action invocation, renderer, watcher, daemon, IPC, networking, MCP surface, UI, new content kind, persistence redesign, or generic document/blob API.
