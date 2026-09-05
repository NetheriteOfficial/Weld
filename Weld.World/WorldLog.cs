namespace Weld.World;

public enum WorldLogLevel { Debug, Info, Warning, Error }

public interface IWorldLog
{
    void Log(WorldLogLevel level, string message);
}

public static class IWorldLogExtensions
{
    public static void Debug(this IWorldLog log, string msg) => log.Log(WorldLogLevel.Debug, msg);
    public static void Info(this IWorldLog log, string msg) => log.Log(WorldLogLevel.Info, msg);
    public static void Warn(this IWorldLog log, string msg) => log.Log(WorldLogLevel.Warning, msg);
    public static void Error(this IWorldLog log, string msg) => log.Log(WorldLogLevel.Error, msg);
}

public sealed class NullWorldLog : IWorldLog
{
    public static readonly NullWorldLog Instance = new();
    public void Log(WorldLogLevel level, string message) { }
}

public sealed class ConsoleWorldLog : IWorldLog
{
    public WorldLogLevel Minimum { get; set; } = WorldLogLevel.Info;
    public void Log(WorldLogLevel level, string message)
    {
        if (level < Minimum) return;
        Console.WriteLine($"[Weld.World/{level}] {message}");
    }
}
