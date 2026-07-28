# Copeland M0 templates

## Language-native structural templates

CTS-TEMPLATE-STATIC-M0 adds the first language-level template surface alongside
the existing `dotnet new` catalog. **Copeland templates are bounded structural
artifact constructors, not a general compile-time programming language.**

```ts
record ConsoleAppConfig { name: string; includeTests: boolean; }

template ConsoleApp<TConfig extends ConsoleAppConfig>(): ProjectTree {
    emit(textFile("Program.cs", `Console.WriteLine("Hello from Copeland template");\n`));
    static if (true) { emit(sourceFile("Copeland/Main.ts", "export const value = 1;\n")); }
}
```

`template` is distinct from `function`: functions are parameterized runtime
code, while templates construct immutable `ProjectTree` values. Template type
parameters use the ordinary declared Copeland record/interface type names; no
second constraint language exists.

M0 static statements are explicit: `static if`, `static match`, and `static
for (const item of finiteArray)`. Static values are literals, immutable records,
template parameters, and arrays whose complete contents are known before the
loop begins. Runtime calls, arbitrary iterators, `while`, recursion,
filesystem/network/process/environment access, clocks, and randomness are
rejected. The compiler never tries to infer whether arbitrary code terminates.

Artifacts are immutable `ProjectTree`, `DirectoryArtifact`, `FileArtifact`,
`TextFileArtifact`, and `SourceFileArtifact` values. Paths are relative,
normalized with `/`, and cannot contain `.` or `..`; duplicate paths are an
error. Text output is UTF-8 without a BOM and LF-normalized. Preview output is
stable JSON with `schemaVersion`, `template`, and ordered `files` entries
(`path`, `kind`, `sha256`, `encoding`, `newlines`).

```console
tscl template preview ConsoleApp.template.ts --entry ConsoleApp --format json
tscl template materialize ConsoleApp.template.ts --entry ConsoleApp --output ./Hello
```

The first command is read-only. The second passes the canonical manifest and
file hashes to TSPack's `materialize-tree` command; TSPack validates, stages,
and commits a new output directory. Copeland does not materialize files or own
package/browser lifecycle behavior.

Template bodies are first bound into a compiler-owned static plan. Artifact
constructor calls have intrinsic identities, and nested template calls carry
resolved template-symbol identities. The evaluator consumes that plan only; it
does not reparse expressions, resolve imports, or make runtime calls. When the
source imports another template, the CLI loads the source directory as the
ordinary Copeland project snapshot, so exports, visibility, aliases, and editor
overlays follow normal module rules.

The local-feed template package supplies a deliberately small catalog:

| Template | Command | Dependency law |
| --- | --- | --- |
| Console | `dotnet new copeland-console -n Example` | CLR only; no TSPack. |
| Library | `dotnet new copeland-library -n Example` | CLR only; no TSPack. |
| React web app | `dotnet new copeland-react -n Example` | TSPack-supervised ASP.NET Core/browser lifecycle; no npm materialization in M0. |
| Mixed workspace | `dotnet new copeland-workspace -n Example` | Run `tscl workspace sync`; conventional TypeScript remains tsc-owned. |

`copeland-react` intentionally uses the smallest browser experience: an
ASP.NET Core host, a React reducer, and a Copeland-compiled API. Its
`tsconfig.tsx` is the Copeland ownership map and its `manifest.tsx` declares the
TSPack `web` RunTarget. TSPack owns host supervision, readiness, browser
inspection, and cleanup. It does not claim to package npm dependencies; TSPack
stays separate from Copeland and will materialize npm only when the template
declares those dependencies.
