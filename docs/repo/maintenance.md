# Repository maintenance

The repository has three main owners: the Copeland compiler, Machina.UI for C#,
and Aurelian application/runtime services. Start at the [documentation index](../INDEX.md)
and [root README](../../README.md). Cross-system adapters belong under
`src/Integrations`; moving a file does not change its semantic owner.

## Find the right lane

| Scope | Maintained entry point |
| --- | --- |
| Compiler, CLI, SDK and selected samples | `Copeland.slnx`; [compiler maintenance](../Copeland/repository-maintenance.md) |
| Core UI contracts | `Machina.UI.slnx`; [UI index](../Machina.UI/README.md) |
| Font diagnostics, reference renderers and galleries | `Machina.UI.Slow.slnx`; [font workflows](../../tools/font-reference/README.md) |
| Runtime, games and renderer adapters | `Aurelian.slnx`; [runtime index](../Aurelian/README.md) |
| Optional inference adapters/proofs | `Aurelian.Inference.slnx`; models/devices have their own prerequisites |
| Selected cross-system build and contract lane | `JointTaskForce.slnx` |
| Explicit integration/sample proofs | `JointTaskForce.Integration.slnx` |
| Product-specific work | `Oblivion.slnx`, `Marionette.slnx`, `TinyFarm.slnx`; consult the owning documentation |

`JointTaskForce.slnx` is a selected cross-system lane, not the union of every
project in the three subsystem solutions. Run the affected subsystem lanes too.
Solution membership and the [test-lane doctrine](../architecture/jtf-test-lane-doctrine.md)
distinguish ordinary contracts from diagnostics, optional tools and hardware proof.
A successful build or headless test does not qualify a native renderer or model.

With .NET 10 installed, build and test each affected lane sequentially:

```powershell
dotnet build Machina.UI.slnx -m:1 -nr:false
dotnet test Machina.UI.slnx --no-build -m:1 -nr:false
```

Replace the solution with the relevant lane above. Do not rebuild assemblies
under an active test process. If an idle MSBuild node holds task assemblies,
use `dotnet build-server shutdown`; `-nr:false` avoids reusing that node on the
next serial run. Run affected tools and samples through their own
entry points as well; source inspection alone does not qualify a moved helper.
The [tool index](../../tools/README.md), [game index](../../Games/README.md) and
[Copeland examples](../../samples/copeland-ts/README.md) expose their entry points.

The current cross-system shader suite includes a `WebGpuToolchain` test that
requires Naga, in addition to its DXC/SPIR-V toolchain. Use the existing local
installer and expose its result to the test process:

```powershell
./scripts/install-webgpu-compiler.ps1
$env:AURELIAN_NAGA = Join-Path $PWD 'artifacts/local/webgpu-tools/bin/naga.exe'
dotnet test JointTaskForce.slnx --no-build -m:1 -nr:false
```

The installer pins the repository's tool version. This is a current lane
prerequisite; the desired separation of external-toolchain proof in the test-lane
doctrine has not yet been applied to that test. Do not turn absence into a passing
shader proof.

## Put material with its owner

| Material | Home |
| --- | --- |
| Production behavior and contracts | Owning `src/<subsystem>`; adapters in `src/Integrations` |
| Repeated runtime/application behavior | Existing owner or `libraries`, with one public/internal contract and real consumers |
| Focused regression fixtures and assertions | Owning `tests` project; check that the file is actually compiled |
| Current application examples | `Games` or `samples`, with prerequisites and a runnable README |
| Maintained qualification/export infrastructure | `tools`, named for its responsibility and shared by thin entry points |
| Current guidance | Owning documentation index/reference page |
| Milestone claims, old measurements and decisions | Historical docs, with limits and recovery provenance |
| Generated reports, hosts, browser captures and scratch | Ignored `artifacts/local/<purpose>`; see [retention policy](artifact-retention.md) |

Promote a repeated implementation to the owner that already has authority.
Keep framework adapters separate from application state and compiler semantics.
Do not make three near-identical helpers into three libraries or retain a copied
production implementation solely to service a diagnostic test.

## Apply 5S without losing behavior

Trace callers, project/solution inclusion, scripts, docs and serialized consumers
before removing or renaming code. Compare duplicated implementations before
choosing one. Preserve unique observable assertions and real input assets; an old
milestone suffix alone is not evidence of obsolescence. Note compatibility impact
when deleting an exported type, even when repository searches find no callers.

Keep a bounded change reviewable: consolidate first, execute the affected real
path, and only then retire redundant copies. Record what remains deliberately
unqualified. Do not sweep ignored directories: they also hold costly models,
toolchains and user work. Root `cleanup.ps1` is user-authored, not maintained
repository cleanup infrastructure.

The [artifact guard](../../tools/Test-ArtifactBudget.ps1) and its
[regression cases](../../tests/Repository/Test-ArtifactBudget.ps1) enforce tracked
output policy. Other topology/dependency checks report their actual findings;
cleanup must not suppress ownership violations with blanket exceptions.

The [repo-wide 5S record](../audits/repository-5s-pass3.md) records the current pass;
older [Copeland cleanup history](../Copeland/reviews/copeland-ts-repository-5s-audit.md)
and [Oblivion artifact history](repository-hygiene-current-proof-policy.md) remain
historical evidence, not current tree-size or qualification claims.

## Local persistence dependency

`Aurelian.GameSaves` consumes the sibling Deliverance source project. Its
MessagePack dependency is owned there. Deliverance now pins **3.1.9**, the patched
version for [GHSA-qhrr-8q5h-9q3h](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h).
This replaces the earlier 3.1.8 restore blocker without a Copeland-side package
override or audit suppression. Keep the authorized Deliverance change with that
repository; committing Copeland documentation alone does not update the sibling.
