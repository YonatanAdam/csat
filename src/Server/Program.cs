using csat;
using Utils;

public class Program
{
    static async Task Main(string[] args)
    {
        ChatServer server = new ChatServer(Global.ADDRESS, Global.PORT);
        await server.StartAsync();
    }
}