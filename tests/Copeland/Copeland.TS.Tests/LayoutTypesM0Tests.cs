using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LayoutTypesM0Tests
{
    [Fact]
    public void Layout_type_enforces_closed_named_topology_and_exposes_an_inferred_shape()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            layout type TwoColumnShell {
                row root {
                    column left;
                    column right;
                }
            }

            layout Valid<0px, 0px> satisfies TwoColumnShell {
                width: 1200px;
                height: 800px;
                row root {
                    column left { width: 256px; height: fill; }
                    column right { width: fill; height: fill; }
                }
            }
            """, "Valid.layout.ts");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.IsType<LayoutTypeDeclarationSyntax>(compilation.SyntaxTree.Root.Members.First());
        BoundLayoutDeclaration layout = compilation.Layouts["Valid"];
        Assert.True(layout.Satisfaction!.IsSatisfied);
        Assert.Equal("TwoColumnShell", layout.Satisfaction.ContractName);
        Assert.Equal("root", layout.Satisfaction.InferredShape.Children.Single().Name);
        Assert.Contains("left", LayoutDataCompiler.ProjectReact(layout).ClassesBySlot.Keys);
        Assert.Contains("right", LayoutDataCompiler.ProjectReact(layout).ClassesBySlot.Keys);
    }

    [Theory]
    [InlineData("column left;", "COPE-LAYOUT-TYPE-0012")]
    [InlineData("column left; column right; column third;", "COPE-LAYOUT-TYPE-0014")]
    [InlineData("column left; row right;", "COPE-LAYOUT-TYPE-0010")]
    public void Layout_type_reports_missing_extra_and_wrong_kind_children(string actualChildren, string expectedDiagnostic)
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile($$"""
            layout type TwoColumnShell {
                row root {
                    column left;
                    column right;
                }
            }
            layout Invalid<0px, 0px> satisfies TwoColumnShell {
                width: 1200px;
                height: 800px;
                row root { {{actualChildren}} }
            }
            """, "Invalid.layout.ts");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expectedDiagnostic);
        Assert.False(compilation.Layouts["Invalid"].Satisfaction!.IsSatisfied);
    }

    [Fact]
    public void Layout_type_checks_nested_topology_and_grid_tracks_separately_from_child_count()
    {
        LayoutDataCompilation compilation = LayoutDataCompiler.Compile("""
            layout type PageShell {
                row root {
                    column main {
                        slot hero;
                        grid features { columns: 4; }
                        slot footer;
                    }
                }
            }
            layout Invalid<0px, 0px> satisfies PageShell {
                width: 1200px;
                height: 800px;
                row root {
                    column main {
                        slot hero { height: 10px; }
                        grid features {
                            columns: 3;
                            slot first;
                            slot second;
                            slot third;
                            slot fourth;
                            slot fifth;
                        }
                        slot footer { height: 10px; }
                    }
                }
            }
            """, "Nested.layout.ts");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-TYPE-0011");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-TYPE-0014");
    }

    [Fact]
    public void Layout_type_reports_duplicate_children_and_track_count_does_not_create_item_count_constraints()
    {
        LayoutDataCompilation duplicate = LayoutDataCompiler.Compile("""
            layout type Shell { row root { column left; column right; } }
            layout Duplicate<0px, 0px> satisfies Shell {
                width: 1200px;
                height: 800px;
                row root {
                    column left { width: 100px; height: fill; }
                    column left { width: 100px; height: fill; }
                    column right { width: fill; height: fill; }
                }
            }
            """, "Duplicate.layout.ts");
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Id == "COPE-LAYOUT-TYPE-0013");

        LayoutDataCompilation tracks = LayoutDataCompiler.Compile("""
            layout type FeatureGrid { grid features { columns: 4; } }
            layout Grid<0px, 0px> satisfies FeatureGrid {
                width: 800px;
                height: 400px;
                grid features { columns: 4; }
            }
            """, "Grid.layout.ts");
        Assert.True(tracks.Success, string.Join(Environment.NewLine, tracks.Diagnostics));
        Assert.True(tracks.Layouts["Grid"].Satisfaction!.IsSatisfied);
    }

    [Fact]
    public void Exported_layout_types_use_normal_imports_and_aliases()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Shell.layout-type.ts", "Shell.layout-type.ts", """
                export layout type DesktopShell {
                    row root {
                        column sidebar;
                        column main;
                    }
                }
                """),
            new CopelandProjectSource("Desktop.layout.ts", "Desktop.layout.ts", """
                import { DesktopShell as Shell } from "./Shell.layout-type";
                export layout Desktop<0px, 0px> satisfies Shell {
                    width: 1200px;
                    height: 800px;
                    row root {
                        column sidebar { width: 256px; height: fill; }
                        column main { width: fill; height: fill; }
                    }
                }
                """),
        ]);

        Assert.Empty(project.Diagnostics);
        CopelandProjectModuleCompilation shell = project.Modules.Single(module => module.LogicalPath == "Shell.layout-type.ts");
        Assert.IsType<LayoutTypeSymbol>(shell.BoundCompilation!.ModuleScope!.Declarations["DesktopShell"]);
        CopelandProjectModuleCompilation desktop = project.Modules.Single(module => module.LogicalPath == "Desktop.layout.ts");
        LayoutSymbol layout = Assert.IsType<LayoutSymbol>(desktop.BoundCompilation!.ModuleScope!.Declarations["Desktop"]);
        Assert.True(layout.BoundLayout!.Satisfaction!.IsSatisfied);
    }

    [Theory]
    [InlineData("type Shell = { name: string; };", "COPE-LAYOUT-TYPE-0016")]
    [InlineData("interface Shell { name: string; }", "COPE-LAYOUT-TYPE-0017")]
    public void Ordinary_types_and_runtime_interfaces_are_not_layout_contracts(string declaration, string expectedDiagnostic)
    {
        CopelandCompilation compilation = CopelandCompiler.Compile($$"""
            {{declaration}}
            layout Desktop<0px, 0px> satisfies Shell {
                width: 100px;
                height: 100px;
                slot root;
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expectedDiagnostic);
    }

    [Fact]
    public void Canonical_layout_type_fixtures_prove_exact_topology_and_module_imports()
    {
        string root = FindRepositoryRoot();
        string fixtureRoot = Path.Combine(root, "samples", "copeland-ts", "machina-layout-types-m0");
        foreach (string fixture in Directory.GetFiles(fixtureRoot, "valid.layout.ts", SearchOption.AllDirectories))
        {
            LayoutDataCompilation compilation = LayoutDataCompiler.Compile(File.ReadAllText(fixture), fixture);
            Assert.True(compilation.Success, fixture + Environment.NewLine + string.Join(Environment.NewLine, compilation.Diagnostics));
        }

        string importedRoot = Path.Combine(fixtureRoot, "07-imported-contract");
        CopelandProjectCompilation imported = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Shell.layout-type.ts", Path.Combine(importedRoot, "Shell.layout-type.ts"), File.ReadAllText(Path.Combine(importedRoot, "Shell.layout-type.ts"))),
            new CopelandProjectSource("Desktop.layout.ts", Path.Combine(importedRoot, "Desktop.layout.ts"), File.ReadAllText(Path.Combine(importedRoot, "Desktop.layout.ts"))),
        ]);
        Assert.Empty(imported.Diagnostics);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
