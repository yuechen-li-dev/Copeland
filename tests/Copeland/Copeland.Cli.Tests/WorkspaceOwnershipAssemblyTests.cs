using Copeland.Cli;
using System.Reflection;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed class WorkspaceOwnershipAssemblyTests
{
    private static readonly string[] PublicWorkspaceContractTypeNames =
    [
        "Copeland.Cli.CopelandWorkspaceOwnership",
        "Copeland.Cli.CopelandWorkspaceOwnershipResult",
        "Copeland.Cli.CopelandWorkspaceTarget",
        "Copeland.Cli.CopelandWorkspaceOwnedSource",
        "Copeland.Cli.CopelandWorkspaceOwnershipDiagnostic",
    ];

    [Fact]
    public void Workspace_ownership_contract_has_one_assembly_owner()
    {
        Assembly workspaceAssembly = typeof(CopelandWorkspaceOwnership).Assembly;
        Assembly cliAssembly = Assembly.Load("Copeland.Cli");

        Assert.Equal("Copeland.TS.Workspace", workspaceAssembly.GetName().Name);

        foreach (string typeName in PublicWorkspaceContractTypeNames)
        {
            Assert.NotNull(workspaceAssembly.GetType(typeName, throwOnError: false));
            Assert.Null(cliAssembly.GetType(typeName, throwOnError: false));
        }
    }
}
