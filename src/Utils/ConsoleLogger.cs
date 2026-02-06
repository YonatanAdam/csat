namespace Utils;

public class ConsoleLogger : ILogger
{
    private readonly string _categoryName;
    private readonly LogLevel _minLevel;

    public ConsoleLogger(string categoryName, LogLevel minMinLevel = LogLevel.Info)
    {
        _categoryName = categoryName;
        _minLevel = minMinLevel;
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel) return;

        var timeStamp = DateTime.Now.ToString("HH:mm:ss");
        var levelStr = level switch
        {
            LogLevel.Debug => "Debug:",
            LogLevel.Info => "Info",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            _ => "???"
        };

        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{timeStamp} ");
        Console.ForegroundColor = color;
        Console.Write($"({_categoryName}) {levelStr}: ");
        Console.ForegroundColor = originalColor;
        Console.WriteLine(message);
    }

    public void Log(LogLevel level, string message, Exception ex)
    {
        Log(level, $"{message} - Exception: {ex}");
        if (level >= LogLevel.Error)
        {
            Log(level, ex.StackTrace ?? "No stack trace available");
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Error(string message, Exception ex) => Log(LogLevel.Error, message, ex);
}