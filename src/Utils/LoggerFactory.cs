namespace Utils;

public class LoggerFactory
{
    private static LogLevel _globalMinLevel = LogLevel.Info;

    public static void SetMinimumLevel(LogLevel level)
    {
        _globalMinLevel = level;
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return new ConsoleLogger(categoryName, _globalMinLevel);
    }

    public static ILogger CreateLogger<T>()
    {
        return new ConsoleLogger(typeof(T).Name, _globalMinLevel);
    }
}