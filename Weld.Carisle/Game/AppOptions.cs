using Weld.Entities;

namespace Weld.Carisle.Game;

public sealed class AppOptions
{
    public NetRole Role { get; set; } = NetRole.Host;
    public ushort Port { get; set; } = 7777;
    public string Address { get; set; } = "127.0.0.1:7777";
    public string PlayerName { get; set; } = Environment.UserName;
    public ushort MaxClients { get; set; } = 16;
    public bool Ps2Streaming { get; set; } = true;
    public string DatPath { get; set; } = "data/carringtonisland.dat";

    public static AppOptions Parse(string[] args)
    {
        var o = new AppOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--server":
                case "--dedicated":
                    o.Role = NetRole.DedicatedServer;
                    if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out var p)) { o.Port = p; i++; }
                    break;
                case "--host":
                    o.Role = NetRole.Host;
                    if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out var hp)) { o.Port = hp; i++; }
                    break;
                case "--connect":
                    o.Role = NetRole.Client;
                    o.Address = Next() ?? o.Address;
                    if (!o.Address.Contains(':')) o.Address += ":" + o.Port;
                    break;
                case "--name":
                    o.PlayerName = Next() ?? o.PlayerName;
                    break;
                case "--max":
                    if (ushort.TryParse(Next(), out var m)) o.MaxClients = m;
                    break;
                case "--modern":
                    o.Ps2Streaming = false;
                    break;
                case "--dat":
                    o.DatPath = Next() ?? o.DatPath;
                    break;
            }
        }
        return o;
    }

    public const string Usage = "Weld.Carisle [--host [port]] [--server [port]] [--connect ip[:port]] [--name NAME] [--max N] [--modern] [--dat path]";
}
