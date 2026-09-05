using System.Diagnostics;
using Weld.Entities;
using Weld.Networking;
using Weld.World;

namespace Weld.Carisle.Game;

public static class DedicatedServerApp
{
    public static int Run(AppOptions options)
    {
        var log = new ConsoleWorldLog { Minimum = WorldLogLevel.Info };
        using var level = GameBootstrap.LoadLevel(options, log);
        using var world = GameBootstrap.CreateWorld(level, NetRole.DedicatedServer, log);
        using var server = new GameServer(world, log);
        GameBootstrap.SpawnDemoProps(world);
        server.Start(options.Port, options.MaxClients);
        world.Entities.TickEnded += _ => server.Update();

        var running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalSeconds;
        var report = 0.0;
        log.Info($"Dedicated server running at {EntityGraph.TickRate} Hz. Ctrl+C to stop.");
        while (running)
        {
            var now = clock.Elapsed.TotalSeconds;
            var dt = now - last;
            last = now;
            world.Advance(dt);
            report += dt;
            if (report >= 5)
            {
                report = 0;
                log.Info($"tick {world.Entities.Tick} | {server.ClientCount} client(s) | {world.Entities.Count} entities | step {world.Entities.LastStepMilliseconds:F2} ms");
            }
            var next = (world.Entities.Tick + 1) * (double)EntityGraph.FixedDeltaConst;
            var sleep = next - clock.Elapsed.TotalSeconds;
            if (sleep > 0.002) Thread.Sleep((int)(sleep * 1000) - 1);
        }
        server.Stop();
        return 0;
    }
}
