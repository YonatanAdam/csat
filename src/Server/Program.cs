using csat;
using Utils;

public class Program
{
    static async Task Main(string[] args)
    {
        LoggerFactory.SetMinimumLevel(LogLevel.Info);
        
        var logger =  LoggerFactory.CreateLogger<ChatServer>();
        
        ChatServer server = new ChatServer(Global.ADDRESS, Global.PORT, logger);
        await server.StartAsync();
    }
}