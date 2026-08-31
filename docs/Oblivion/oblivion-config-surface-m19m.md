# Oblivion configuration surface — M19m

## Ownership and scope

M19m defines one application/user configuration, separate from Workspace/Page/Card truth and from process-local commands. The production path is `%APPDATA%\Oblivion\config.toml` (`C:\Users\<user>\AppData\Roaming\Oblivion\config.toml` on Windows). Tests inject an isolated file path into the same `OblivionConfigStore` implementation.

A missing file yields typed defaults in memory and does not create a file. The first successful `config set` creates the parent directory and complete file. There is no workspace, environment, profile, or command-line precedence layer.

## Format and typed model

The deterministic TOML file is:

```toml
appearance = "system"
newline = "preserve"
style = "default"
```

`OblivionConfig` contains `OblivionAppearance`, `OblivionNewlinePolicy`, and `OblivionStyleProfile`. CLI keys and values are decoded at the App boundary; product code does not use a dynamic settings dictionary. Unknown keys, duplicate keys, malformed assignments, and invalid enum values fail explicitly.

| Key | Typed values | Default | M19m consumption |
| --- | --- | --- | --- |
| `appearance` | `system`, `light`, `dark` | `system` | `CONFIG_ONLY` |
| `newline` | `preserve`, `lf`, `crlf` | `preserve` | applied to stack mutation |
| `style` | `default` | `default` | contract identity only |

Appearance is global application policy, but applying it would require a real light/system palette in the currently dark-only standalone style. M19m therefore does not pretend that changing the key changes the UI. Style exposes only the honest existing default identity; no compact/comfortable presets were invented. Card height is omitted: current height is derived from viewport/layout and has no evidence-backed global persistence owner.

## CLI mapping

`oblivion config show` prints all three values. `config get <key>` prints only the raw value in human mode. `config set <key> <value>` validates through the typed store and prints `key = value`. JSON show uses `{appearance,newline,style}`; get uses `{key,value}`; set adds `succeeded: true`. Failures return structured diagnostics and product exit code 1.

Writes use a same-directory temporary file, serialize the complete typed config, read and validate that temporary file, then atomically move it over the destination. A failed write leaves no accepted partial configuration.

The future Settings UI should consume this same typed model and store. M19m adds no Settings UI.
