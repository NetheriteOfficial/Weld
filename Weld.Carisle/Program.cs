using Weld.Carisle.Game;
using Weld.Entities;

namespace Weld.Carisle;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            Console.WriteLine(AppOptions.Usage);
            return 0;
        }
        var options = AppOptions.Parse(args);
        return options.Role == NetRole.DedicatedServer
            ? DedicatedServerApp.Run(options)
            : new ClientApp(options).Run();
    }
}
