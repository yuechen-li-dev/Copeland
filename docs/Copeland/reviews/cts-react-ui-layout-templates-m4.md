# CTS-REACT-UI-LAYOUT-TEMPLATES-M4

Status: success.

Layout templates and immutable spatial variants now use the real Copeland
compiler and React projection. This continues the
[M3 contribution-stream pass](cts-react-ui-grammar-m3.md).

## Authoring law

The existing template prefix keeps static configuration separate from the
layout's coordinate origin:

```ts
export template<static gap: Length = 12px, static padding: Length = 0px>
layout Reading<0px, 0px> {
    width: fill;
    height: fit;
    column root {
        gap: gap;
        padding: padding;
        slot heading { height: fit; }
        slot body { height: fit; }
        slot notes { height: fit; }
    }
}

layout ArticleReading = instantiate Reading<> with { root.padding: 16px; };
layout DenseReading = ArticleReading with { root.gap: 8px; root.padding: 12px; };
layout FloatingReading<20px, 10px> = DenseReading;
```

Templates construct closed spatial data during compilation. Derived declarations
inherit origin; an explicit coordinate pair overrides it. A directly authored
layout body, including a template body, still requires an origin. There is no
invented placement default and no runtime template specialization.

`Length` uses ordinary immutable record representation with a numeric quantity
and its px/ui unit. Template parameters, static record configurations, record
`with`, earlier-parameter defaults, and eligible ordinary functions pass through
the existing bounded static evaluator. Origin coordinates preserve their own
unit rules: `4ui` is a valid authored coordinate, while a geometric ui literal
retains Machina's [0, 1] restriction. Padding currently requires px under the
existing inset representation; unsupported units diagnose instead of disappearing.

Ordinary configuration records can select structure:

```ts
record Config { gap: Length; compact: boolean; }
const Comfortable: Config = { gap: 12px, compact: false };
const Compact = Comfortable with { gap: 8px, compact: true };

template<static config: Config = Comfortable>
layout Panels<0px, 0px> {
    width: fill;
    height: fit;
    static if (config.compact) {
        column root {
            gap: config.gap;
            slot list { height: fit; }
            slot detail { height: fit; }
        }
    } else {
        row root {
            gap: config.gap;
            slot list { width: 300px; height: fit; }
            slot detail { width: fill; height: fit; }
        }
    }
}

layout Wide = instantiate Panels<>;
layout Narrow = instantiate Panels<config: Compact>;
```

The maintained TwoPane template uses an arrangement enum and an ordinary
exhaustive `match` expression as its static condition. Runtime application state
selects between its closed horizontal and vertical specializations. Contributions
retain M3 construction timing and are not rebuilt by binding.

Member-path `with` updates copy existing spatial nodes. Paths follow the actual
hierarchy, for example `root.list.width`. Unknown paths, invalid properties,
wrong units/types, duplicate/overlapping updates, invalid styles, and invalid
grid counts diagnose. Updates preserve topology and retain unrelated values.
Style records are replaced explicitly; they are not implicitly deep-merged.
This path notation is currently a layout surface, not a new general record-path
update grammar.

Each specialization and variant goes through the same spatial validator and
normalizer. Exact `layout type` contracts remain exact. A derived graph retains
the bound contract authority, including a private contract in the template's
origin module; deriving a layout does not require importing that private type.
Changing structure belongs to a template specialization, not `with`.

## Compiler ownership

- Template-prefixed layouts remain ordinary `TemplateSymbol` declarations with
  typed plans, named static arguments, defaults, constraints, imports and aliases.
- `BoundTemplateLayoutConstruction` contains typed values and finite spatial
  branches. Evaluation never receives layout syntax or generates source text.
- `LayoutConstruction`, `LayoutNodeData`, and `LayoutPropertyData` are closed
  spatial inputs. Handwritten source adapts to the same property/node validators
  used by template results. The existing Machina normalizer and React emitter
  remain the presentation path.
- Static calls use ordinary function binding, effect classification, and the
  existing evaluator. Forwarded imported templates retain transitive private
  helper environments. Runtime state and host effects remain unavailable.
- Layout result hashes include nested geometry. Layouts and template plans do
  not become runtime UI objects; Length configurations use existing nominal
  record carriers when ordinary runtime code needs them.
- LSP declaration labels, template parameters, and spatial nodes recognize the
  new prefix. Existing origin diagnostics and legacy inline streams remain.

## Motivating case and qualification

The real friction application imports Reading and TwoPane from
[Layouts.ts](../../../samples/copeland-ts/react-friction-m0/src/Layouts.ts).
Its three concrete arrangements are now three specialization declarations,
including the article's padding update. Application-module source drops from
208 to 185 nonblank/noncomment lines. The reusable library costs 35 lines;
total authored application source increases from 275 to 287 versus the baseline's
175. This demonstrates reduced application-site ceremony and reusable geometry,
not a universal source-size or timed usability advantage.

The compiler corpus retains the previous 18 cases and adds nine layout-template
and variant cases. Browser qualification covers the same 74 paired observations,
including live resize, selection retention, controls, validation, Base UI dialog
focus/Escape/reopening, long copy, 200% text, and fallback fonts. All 12 paragraphs
on the original website remain reachable. The deliberate 180px visible-overflow
specimen retains its documented behavior.

The final `dotnet build Copeland.slnx -m:1 -v:minimal` passes with zero warnings
and errors. `dotnet test Copeland.slnx --no-build -m:1` passes **2,178 tests**
across eight assemblies, with zero failed or skipped. The initial full run also
passed; final closeout follows the imported-contract and style-validation fixes.
Twenty focused layout-template cases cover configuration records, imports and
aliases, private helpers and contracts, transitive forwarding, dependent defaults,
origins, immutable variants, invalid inputs, and deterministic nested-geometry
hashes. An additional C# execution test qualifies Length configuration records
and immutable `with` updates through generated, compiled code.

All 27 compiler-probe expectations match. The 74 paired browser observations
pass in Microsoft Edge 153.0.4234.48, with zero console/page/request diagnostics.
Compiler assembly and all 13 browser source/artifact hashes match the working
files after final qualification.

Tracked evidence:

- [Compiler probes](evidence/react-ui-layout-templates-m4/compiler-report.json)
- [Browser observations](evidence/react-ui-layout-templates-m4/browser-report.json)
- [Website regression](evidence/react-ui-layout-templates-m4/website-regression.json)
- [Source accounting](evidence/react-ui-layout-templates-m4/authoring-accounting.json)
- [Full validation](evidence/react-ui-layout-templates-m4/validation.json)

Ignored screenshots live in
`samples/copeland-ts/react-friction-m0/artifacts/react-ui-layout-templates-m4/screenshots`.
C# storage execution is not a C# UI qualification.

## Explicit boundaries

Semantic layer references and relative derivations inside layout templates remain
diagnosed boundaries; their existing concrete-layout paths remain available.
Paint/text-policy replacements through `with` are diagnosed; configure those
policies in the layout body. Root/body dimension modes `fill` and `fit` remain
supported, while general sizing-mode parameter polymorphism is not introduced.
Layout-specific static selection admits named nodes; finite node generation,
generic spatial contracts, runtime geometry, dynamic contribution collections,
and generalized React state retention are not claims of this increment.

## Reproduce

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1 -LayoutTemplates -FullValidation
```

The default run selects M4 evidence. Earlier M0/M1/M3 evidence is retained.
Browser servers are ephemeral and closed by the existing qualification scripts.
