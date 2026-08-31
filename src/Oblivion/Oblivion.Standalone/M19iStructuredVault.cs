namespace Oblivion.Standalone;

public static class M19iStructuredVault
{
    public const string DirectoryName = "M19iNotebook.oblivion";
    public const string WorkspaceId = "m19i-notebook";
    public const string PageId = "notebook";
    public const string FirstCardId = "physical-atom";
    public const string SecondCardId = "notebook-stack";

    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, DirectoryName);
}
