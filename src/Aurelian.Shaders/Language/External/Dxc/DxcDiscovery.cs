namespace Aurelian.Shaders.Language.External.Dxc;

public static class DxcDiscovery
{
    public static DxcExecutable? FindDxc()
    {
        var resolution = DxcExecutableResolver.Resolve();
        return resolution.Success ? new DxcExecutable(resolution.ExecutablePath!) : null;
    }
}
