namespace Oblivion.App;

public static class Program
{
    public static int Main(string[] args)
    {
        return new OblivionCommandLine(Console.Out, Console.Error).Run(args);
    }
}
