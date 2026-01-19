using System.Net.Sockets;
using System.Net;
using System.Threading.Channels;
using _4at;
using System.Text;

public static class Global
{
    public static bool SAFE_MODE = false;
    public static int port = 6969;
    public static string address = "127.0.0.1";
    public static TimeSpan BAN_LIMIT = TimeSpan.FromMinutes(10);
}

public class Sensitive<T>
{
    public T Inner;

    public Sensitive(T inner) => Inner = inner;

    public override string ToString() => Global.SAFE_MODE ? "[REDACTED]" : $"{this.Inner}";
}

public static class SensetiveExtensions
{
    public static Sensitive<T> AsSensitive<T>(this T value) => new Sensitive<T>(value);
}

public abstract record Message
{
    public record ClientConnected(TcpClient client) : Message;
    public record ClientDisconnected(TcpClient client) : Message;
    public record NewMessage(TcpClient author, Byte[] Data) : Message;
}

public class Program
{

    public static void handle_client(TcpClient client, ChannelWriter<Message> messages)
    {
        if (!messages.TryWrite(new Message.ClientConnected(client)))
        {
            Console.WriteLine("Error: could not write message to user");
        }
        // var msg = Encoding.ASCII.GetBytes("Hallo meine Freunde!\n");
        // stream.Write(msg, 0, msg.Length);
        Byte[] buff = new Byte[64];
        for (; ; )
        {
            try
            {
                int n = client.GetStream().Read(buff);
                if (n == 0)
                {
                    messages.TryWrite(new Message.ClientDisconnected(client));
                    break;
                }

                var rawMsg = Encoding.UTF8.GetString(buff[..n]);
                Console.WriteLine($"INFO: Raw '{rawMsg}'");
                if (!messages.TryWrite(new Message.NewMessage(client, buff[..n])))
                {
                    Console.WriteLine("Error: could not write message to user");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Read operation timed out or was cancelled.");
            }
            catch (IOException ex) when (ex.InnerException is SocketException sx)
            {
                Console.WriteLine($"Network Error: {sx.SocketErrorCode.AsSensitive()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.AsSensitive()}");
            }
        }
    }

    public static async Task server(ChannelReader<Message> messages)
    {
        var clients = new Dictionary<string, Client>();
        var banned = new Dictionary<string, DateTime>();

        for (; ; )
        {
            await foreach (var msg in messages.ReadAllAsync())
            {
                switch (msg)
                {
                    case Message.ClientConnected(var author):
                        {
                            var author_addr = author.Client.RemoteEndPoint!.ToString();
                            var now = DateTime.UtcNow;
                            if (banned.Remove(author_addr!, out DateTime banned_at))
                            {
                                var duration = now - banned_at;

                                if (duration >= Global.BAN_LIMIT)
                                {
                                    banned.Remove(author_addr!);
                                }
                                else
                                {
                                    var timeLeft = Global.BAN_LIMIT - duration;
                                    string ban_msg = $"You are banned MF: {timeLeft.TotalSeconds:F0} secs left\n";

                                    author.GetStream().Write(Encoding.ASCII.GetBytes(ban_msg));
                                    author.Close();
                                    return;
                                }
                            }

                            clients.Add(author_addr!, new Client(author, now, 0));
                            break;
                        }
                    case Message.ClientDisconnected(var author):
                        {
                            var addr = author.Client.RemoteEndPoint!.ToString();
                            clients.Remove(addr!);
                            break;
                        }
                    case Message.NewMessage(var author, var bytes):
                        {
                            if (Encoding.ASCII.GetString(bytes).Trim() == "") break;
                            var author_addr = author.Client.RemoteEndPoint!.ToString();
                            clients.TryGetValue(author_addr!, out var clinet);
                            var now = DateTime.UtcNow;

                            foreach (var (addr, client) in clients)
                            {
                                if (addr != author_addr)
                                {
                                    client.conn.GetStream().Write(bytes);
                                }

                            }
                            break;
                        }
                }
            }
        }
    }

    static async Task Main(string[] args)
    {
        TcpListener? listener = null;
        try
        {
            IPAddress address = IPAddress.Parse(Global.address);
            listener = new TcpListener(address, Global.port);

            listener.Start();
            Console.WriteLine($"Listening on {Global.address.AsSensitive()}:{Global.port.AsSensitive()}...");

            var c = Channel.CreateUnbounded<Message>();
            var (message_sender, message_receiver) = (c.Writer, c.Reader);

            Thread server_thread = new Thread(async () => await server(message_receiver));
            server_thread.Start();

            while (true)
            {
                Console.WriteLine("Waiting for connection...");

                TcpClient client = listener.AcceptTcpClient();

                Console.WriteLine($"client connected");
                Thread t = new Thread(() => handle_client(client, message_sender));
                t.Start();
            }
        }
        catch (ArgumentNullException e)
        {
            Console.WriteLine($"Error: local addr is null: {e.AsSensitive()}");
        }
        catch (ArgumentOutOfRangeException e)
        {
            Console.WriteLine($"Error: unavailable port '{Global.port}': {e.AsSensitive()}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error: {e.AsSensitive()}");
        }
        finally
        {
            listener!.Stop();
        }

    }
}
