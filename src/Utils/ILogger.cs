namespace Utils;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface ILogger
{
    void Log(LogLevel level, string message);
    void Log(LogLevel level, string message, Exception exception);
    
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message);
    void Error(string message, Exception exception);
}