namespace Copeland.TS.LanguageServer;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.Out.WriteLine(LanguageServerHost.Version);
            return 0;
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: tscl language-server [--version]");
            return 2;
        }

        return new LanguageServerHost(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error).Run();
    }
}
