using System.Net.Sockets;
using System.Net;
using System.Threading.Channels;
using _4at;

public static class Global
{
    public static bool SAFE_MODE = false;
    public static int port = 6969;
    public static string address = "127.0.0.1";
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

        for (; ; )
        {
            await foreach (var msg in messages.ReadAllAsync())
            {
                switch (msg)
                {
                    case Message.ClientConnected(var author):
                        {
                            var addr = author.Client.RemoteEndPoint!.ToString();
                            clients.Add(addr!, new Client(author));
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
                            var author_addr = author.Client.RemoteEndPoint!.ToString();
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
