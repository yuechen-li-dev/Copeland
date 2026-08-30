namespace Oblivion.App;

public static class Program
{
    public static int Main(string[] args)
    {
        OblivionProductSurface surface = new(localHost: OblivionSystemHostCapabilities.Create());
        return new OblivionCommandLine(Console.Out, Console.Error, surface).Run(args);
    }
}
