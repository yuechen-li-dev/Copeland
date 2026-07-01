# Copeland Windows Test Triage M5i

## Purpose

M5i removes the unrelated Windows test blockers that were preventing clear full-solution validation after the Machina M5h work.

## Reproduction commands

```powershell
dotnet test Copeland.slnx --logger "console;verbosity=detailed"
dotnet test tests/Copeland.Script.Tests/Copeland.Script.Tests.csproj --logger "console;verbosity=detailed"
dotnet test tests/Copeland.Cli.Tests/Copeland.Cli.Tests.csproj --logger "console;verbosity=detailed"
```

## Initial failing tests

- `Copeland.Cli.Tests.CliIntegrationTests.EmitMirToStdout`
- `Copeland.Cli.Tests.CliIntegrationTests.EmitCSharpToStdout`
- `Copeland.Cli.Tests.CliIntegrationTests.EmitMirToFile`
- `Copeland.Cli.Tests.CliIntegrationTests.EmitCSharpToFile`
- `Copeland.Cli.Tests.CliIntegrationTests.InvalidSourceExitsOneAndDoesNotWriteOutput`
- `Copeland.Cli.Tests.CliIntegrationTests.Ternary_Profile_Ban_ExitsOne_With_Diagnostic`
- `Copeland.Cli.Tests.CliIntegrationTests.MissingEmitExitsTwo`
- `Copeland.Cli.Tests.CliIntegrationTests.UnknownEmitExitsTwo`
- `Copeland.Cli.Tests.CliIntegrationTests.MissingInputFileExitsThree`
- `Copeland.Script.Tests.LexerCorpusTests.Lexer_Corpus_Matches_Expected` for `testdata/m0-lex-invalid/unterminated_comment.cope`
- `Copeland.Script.Tests.ParserCorpusTests.Parser_Corpus_Matches_Expected` for `testdata/m1-enum-parse-invalid/missing_payload_colon.ts`
- `Copeland.Script.Tests.BinderCorpusTests.Binder_Corpus_Matches_Expected` for `testdata/m1-enum-bind-invalid/unknown_case.ts`
- `Copeland.Script.Tests.BinderCorpusTests.Binder_Corpus_Matches_Expected` for `testdata/m1-enum-bind-invalid/payload_missing_args.ts`
- `Copeland.Script.Tests.BinderCorpusTests.Binder_Corpus_Matches_Expected` for `testdata/m0-bind-invalid/assignment_to_const.ts`
- `Copeland.Script.Tests.BinderCorpusTests.Binder_Corpus_Matches_Expected` for `testdata/m0-bind-invalid/eval_banned.ts`
- `Copeland.Script.Tests.MirCorpusTests.Mir_Corpus_Matches_Expected` for `testdata/m0-mir-invalid/null_literal.ts`
- `Copeland.Script.Tests.MirCorpusTests.Mir_Corpus_Matches_Expected` for `testdata/m0-mir-invalid/nested_unhandled_fallible.ts`

## Failure classification

- `Copeland.Cli.Tests.*`: `A. Windows path separator assumption`
  Exact evidence: the tests launched `dotnet run --project /workspace/Copeland/src/Copeland.Cli/Copeland.Cli.csproj`, which is a Unix-only absolute path and caused every CLI invocation to fail before real CLI behavior ran.
- `Copeland.Script.Tests.*` corpus mismatches: `B. Windows newline assumption`
  Exact evidence: failing diagnostics differed only by absolute positions/lengths after CRLF checkout, for example `COPE-PARSE-0004|24|0` vs `25|0` and `COPE-ENUM-0004|65|6` vs `70|6`.

## Fixes applied

- Updated `tests/Copeland.Cli.Tests/CliIntegrationTests.cs` to resolve the CLI project from the real repository root instead of the hardcoded `/workspace/...` path.
- Added `tests/Copeland.Script.Tests/Corpus/CorpusFile.cs` to centralize repository-root lookup and corpus text normalization.
- Updated the script corpus tests to read source files with deterministic LF line endings while preserving source content length.
- Updated corpus expectation normalization to stay shared and explicit across lexer, parser, binder, MIR, and C# corpus tests.

## Remaining blockers

None in the M5i scope. `dotnet test Copeland.slnx` is green on Windows after these changes.

## Validation profiles

- Standard full-repo validation is restored; no fallback Machina-only script/profile was needed.
- Machina milestone validation remains the same focused subset used in M5h:
  - `tests/Machina.Core.Tests`
  - `tests/Machina.Dominatus.Tests`
  - `tests/Machina.Standard.Tests`
  - `tests/Machina.Pipeline.Tests`
  - `tests/Machina.Presenter.Sample.Tests`
  - `samples/Machina.Presenter.Sample`

## Final validation results

- `dotnet test tests/Copeland.Script.Tests/Copeland.Script.Tests.csproj` passed.
- `dotnet test tests/Copeland.Cli.Tests/Copeland.Cli.Tests.csproj` passed.
- `dotnet test tests/Machina.Core.Tests/Machina.Core.Tests.csproj` passed.
- `dotnet test tests/Machina.Dominatus.Tests/Machina.Dominatus.Tests.csproj` passed.
- `dotnet test tests/Machina.Standard.Tests/Machina.Standard.Tests.csproj` passed.
- `dotnet test tests/Machina.Pipeline.Tests/Machina.Pipeline.Tests.csproj` passed.
- `dotnet test tests/Machina.Presenter.Sample.Tests/Machina.Presenter.Sample.Tests.csproj` passed.
- `dotnet build samples/Machina.Presenter.Sample/Machina.Presenter.Sample.csproj` passed.
- `dotnet test Copeland.slnx` passed.
- `dotnet build Copeland.slnx --no-restore` passed.
- `git diff --check` passed.
- `rg -n "Avalonia|Window|Presenter" src/Machina.Layout src/Machina.Core src/Machina.Standard src/Machina.Runtime src/Machina.Dominatus src/Machina.Renderer.Raster src/Machina.Renderer.Raster.Text src/Machina.Renderer.Raster.Dominatus src/Machina.Pipeline` returned no matches.
- `rg -n "ProjectReference.*Dominatus|Dominatus.Core|Dominatus.OptFlow|Dominatus" . -g "*.csproj" -g "*.props" -g "*.targets" -g "*.sln" -g "*.slnx"` confirmed Machina integration packages still use `Dominatus.Core` 0.4.0 and `Dominatus.OptFlow` 0.4.0 from NuGet.

## Conclusion

M5i achieved Outcome A. The unrelated Windows-sensitive blockers were fixed at the test layer, Machina validation stayed clean, the M5h presenter changes remained intact, and full solution validation is restored.
