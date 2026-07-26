using Copeland.TS.MachinaSource;
using Copeland.TS.Mir.Machina;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class MachinaSourceM1Tests
{
    [Fact]
    public void Ordinary_Copeland_functions_lower_units_styles_views_and_stack_geometry_to_Machina_MIR()
    {
        MachinaSourceCompilation compilation = MachinaSourceCompiler.Compile(FunctionScreen, "Settings.ts", "SettingsPage");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message)));
        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(compilation.View!, new MachinaRect(0, 0, 400, 240));

        MachinaResolvedNode status = resolved.Nodes.Single(node => node.Authored.Text == "Status: ready");
        MachinaResolvedNode save = resolved.Nodes.Single(node => node.Authored.EventName == "SettingsEvent.Save");
        MachinaResolvedNode toggle = resolved.Nodes.Single(node => node.Authored.EventName == "SettingsEvent.ToggleDarkMode");
        Assert.Equal(110, status.Frame.X);
        Assert.Equal(20, status.Frame.Y);
        Assert.Equal(108, save.Frame.Height);
        Assert.Equal(24 + 0.25 * (400 - 48) - 2, status.Frame.X);
        Assert.Equal(20, toggle.Frame.Height);
        Assert.Contains("source=Settings.ts:", resolved.ToDebugText(), StringComparison.Ordinal);

        MachinaBrowserArtifact artifact = MachinaBrowserLowerer.Lower(resolved);
        Assert.Contains("data-machina-event=\"SettingsEvent.Save\"", artifact.Html, StringComparison.Ordinal);
        Assert.Contains("left: 86px", artifact.Css, StringComparison.Ordinal);
        Assert.DoesNotContain("display: flex", artifact.Css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display: grid", artifact.Css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ui_range_and_unitless_layout_values_report_authored_source_diagnostics()
    {
        MachinaSourceCompilation invalidUi = MachinaSourceCompiler.Compile("""
            function SettingsPage(): View {
                return Root([Container([], { frame: Absolute({ x: 1.1ui, y: 0px, width: 10px, height: 10px }) })]);
            }
            """, "InvalidUi.ts", "SettingsPage");
        MachinaSourceCompilation unitless = MachinaSourceCompiler.Compile("""
            function SettingsPage(): View {
                return Root([Container([], { frame: Absolute({ x: 10, y: 0px, width: 10px, height: 10px }) })]);
            }
            """, "Unitless.ts", "SettingsPage");

        Assert.Contains(invalidUi.Diagnostics, diagnostic => diagnostic.Id == "COPE-MACHINA-UI-0001" && diagnostic.SourcePath == "InvalidUi.ts");
        Assert.Contains(unitless.Diagnostics, diagnostic => diagnostic.Id == "COPE-MACHINA-LENGTH-0002" && diagnostic.SourcePath == "Unitless.ts");
    }

    [Fact]
    public void TsXml_is_optional_syntax_over_the_same_Machina_view_contract()
    {
        MachinaSourceCompilation functions = MachinaSourceCompiler.Compile(FunctionScreen, "Settings.ts", "SettingsPage");
        MachinaSourceCompilation tsXml = MachinaSourceCompiler.Compile(TsXmlScreen, "Settings.tsx");

        Assert.True(functions.Success, string.Join(Environment.NewLine, functions.Diagnostics));
        Assert.True(tsXml.Success, string.Join(Environment.NewLine, tsXml.Diagnostics));

        MachinaResolvedDocument functionResolved = MachinaLayoutResolver.Resolve(functions.View!, new MachinaRect(0, 0, 400, 240));
        MachinaResolvedDocument xmlResolved = MachinaLayoutResolver.Resolve(tsXml.View!, new MachinaRect(0, 0, 400, 240));
        Assert.Equal(Shape(functionResolved), Shape(xmlResolved));
    }

    [Fact]
    public void Browser_page_wraps_generated_absolute_artifacts_in_a_reducer_owned_event_runtime()
    {
        MachinaSourceCompilation compilation = MachinaSourceCompiler.Compile(FunctionScreen, "Settings.ts", "SettingsPage");
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MachinaResolvedDocument resolved = MachinaLayoutResolver.Resolve(compilation.View!, new MachinaRect(0, 0, 400, 240));

        string page = MachinaBrowserPageBuilder.Create(resolved, "Settings");

        Assert.Contains("function reduce(current, event)", page, StringComparison.Ordinal);
        Assert.Contains("SettingsEvent.Save", page, StringComparison.Ordinal);
        Assert.Contains("position: absolute", page, StringComparison.Ordinal);
        Assert.DoesNotContain("display: flex", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display: grid", page, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> Shape(MachinaResolvedDocument document)
        => document.Nodes.Select(node => $"{node.Identity}|{node.Kind}|{node.Authored.Text}|{node.Authored.EventName}|{node.Frame.X}|{node.Frame.Y}|{node.Frame.Width}|{node.Frame.Height}|{node.MeasurementDependency}").ToArray();

    private const string FunctionScreen = """
        enum SettingsEvent { Save, ToggleDarkMode, }

        const ButtonBase = {
            surface: { fill: "#182238", radius: 8px },
            text: { color: "#ffffff", weight: 600 },
            border: { width: 1px, color: "#334155", style: "solid" }
        };
        const PrimaryButton = ButtonBase with {
            surface: { fill: "#2563eb" }
        };

        function SettingsPanel(): View {
            return VStack(
                [
                    Text("Status: ready", {
                        main: Fixed(40px),
                        cross: Fill(),
                        offset: { x: 0.25ui - 2px },
                        wrap: TextWrap.Word
                    }),
                    Button("Save", SettingsEvent.Save, {
                        main: Fill(),
                        cross: Fill(),
                        style: PrimaryButton
                    }),
                    Toggle(false, SettingsEvent.ToggleDarkMode, {
                        main: Fixed(20px),
                        cross: Fill()
                    })
                ],
                {
                    frame: Anchor({ left: 24px, right: 24px, top: 20px, bottom: 20px }),
                    gap: 16px
                }
            );
        }

        function SettingsPage(): View {
            return Root([SettingsPanel()]);
        }
        """;

    private const string TsXmlScreen = """
        enum SettingsEvent { Save, ToggleDarkMode, }

        const ButtonBase = {
            surface: { fill: "#182238", radius: 8px },
            text: { color: "#ffffff", weight: 600 },
            border: { width: 1px, color: "#334155", style: "solid" }
        };
        const PrimaryButton = ButtonBase with {
            surface: { fill: "#2563eb" }
        };

        export default <Root>
            <VStack frame={Anchor({ left: 24px, right: 24px, top: 20px, bottom: 20px })} gap={16px}>
                <Text main={Fixed(40px)} cross={Fill()} offset={{ x: 0.25ui - 2px }} wrap={TextWrap.Word}>Status: ready</Text>
                <Button main={Fill()} cross={Fill()} style={PrimaryButton} onClick={SettingsEvent.Save}>Save</Button>
                <Toggle value={false} main={Fixed(20px)} onChange={SettingsEvent.ToggleDarkMode} />
            </VStack>
        </Root>;
        """;
}
