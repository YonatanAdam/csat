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
    public static TimeSpan MESSAGE_RATE = TimeSpan.FromSeconds(1);
    public static int STRIKE_LIMIT = 10;
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
    public record ClientDisconnected(string author_addr) : Message;
    public record NewMessage(string author_addr, Byte[] Data) : Message;
}

public class Program
{

    public static bool isValidAscii(byte[] bytes)
    {
        try
        {
            Encoding.ASCII.GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void handle_client(TcpClient client, ChannelWriter<Message> messages)
    {
        if (!messages.TryWrite(new Message.ClientConnected(client)))
        {
            Console.WriteLine("Error: could not write message to user");
        }

        var author_addr = client.Client.RemoteEndPoint!.ToString();
        if (author_addr == null)
        {
            Console.WriteLine("Error: could not resolve client address");
            return;
        }
        Byte[] buff = new Byte[64];

        for (; ; )
        {

            if (!client.Connected) break;
            try
            {
                int n = client.GetStream().Read(buff);
                if (n > 0)
                {

                    if (!messages.TryWrite(new Message.NewMessage(author_addr, buff[..n])))
                    {
                        Console.WriteLine("Error: could not write message to the server thread");
                    }
                }
                else
                {
                    if (!messages.TryWrite(new Message.ClientDisconnected(author_addr)))
                    {
                        Console.WriteLine($"Error: could not send disconnected msg {author_addr}");
                    }
                    break;

                }

            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Read operation timed out or was cancelled.");
            }
            catch (IOException ex) when (ex.InnerException is SocketException sx)
            {
                if (sx.SocketErrorCode == SocketError.ConnectionAborted)
                {
                    Console.WriteLine($"INFO: Connection closed for {author_addr} (likely banned)");
                }
                else
                {
                    Console.WriteLine($"Network Error: {sx.SocketErrorCode.AsSensitive()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.AsSensitive()}");
                break;
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
                                var diff = now - banned_at;

                                if (diff >= Global.BAN_LIMIT)
                                {
                                    banned.Remove(author_addr!);
                                }
                                else
                                {
                                    var timeLeft = Global.BAN_LIMIT - diff;
                                    Console.WriteLine($"INFO: Client {author_addr} tried to connect while being banned for {timeLeft.TotalSeconds:F0} more secs");
                                    string ban_msg = $"You are banned MF: {timeLeft.TotalSeconds:F0} secs left\n";
                                    try
                                    {
                                        author.GetStream().Write(Encoding.ASCII.GetBytes(ban_msg));
                                        banned.Add(author_addr, now);
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine($"Error: could not send ban msg: {e}");
                                    }
                                    finally
                                    {
                                        author.Close();
                                    }
                                    break;
                                }
                            }

                            clients[author_addr] = new Client(author, now, 0);
                            Console.WriteLine($"INFO: Client {author_addr} connected");
                            break;
                        }
                    case Message.ClientDisconnected(var author_addr):
                        {
                            Console.WriteLine($"INFO: Cient {author_addr} disconnected");
                            clients.Remove(author_addr!);
                            break;
                        }
                    case Message.NewMessage(var author_addr, var bytes):
                        {
                            var text = Encoding.ASCII.GetString(bytes);
                            if (clients.TryGetValue(author_addr!, out var author))
                            {
                                var now = DateTime.UtcNow;
                                var diff = now - author.last_messsage;

                                if (diff >= Global.MESSAGE_RATE)
                                {
                                    if (isValidAscii(bytes) && !string.IsNullOrWhiteSpace(text))
                                    {

                                        Console.WriteLine($"INFO: Client {author_addr} sent message: [{string.Join(", ", bytes)}]");

                                        foreach (var (addr, client) in clients)
                                        {
                                            if (addr != author_addr)
                                            {
                                                try
                                                {

                                                    client.conn.GetStream().Write(bytes);
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"Error: could not broadcast message to all the clients from {author_addr}: {e}");
                                                }
                                            }

                                        }
                                    }
                                    else
                                    {
                                        author.strike_count += 1;
                                        if (author.strike_count >= Global.STRIKE_LIMIT)
                                        {
                                            Console.WriteLine($"INFO: Client {author_addr} got banned");
                                            banned[author_addr] = now;
                                            var ban_msg = Encoding.ASCII.GetBytes("You are banned MF\n");
                                            try
                                            {

                                                author.conn.GetStream().Write(ban_msg);
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine($"Error: could not send banned msg to {author_addr}: {e}");
                                            }
                                            finally
                                            {
                                                clients.Remove(author_addr);
                                                author.conn.Close();
                                            }
                                        }

                                    }
                                }
                                else
                                {
                                    author.strike_count += 1;
                                    if (author.strike_count >= Global.STRIKE_LIMIT)
                                    {
                                        Console.WriteLine($"INFO: Client {author_addr} got banned");
                                        banned[author_addr] = now;
                                        var ban_msg = Encoding.ASCII.GetBytes("You are banned MF\n");
                                        try
                                        {

                                            author.conn.GetStream().Write(ban_msg);
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine($"Error: could not send banned msg to {author_addr}: {e}");
                                        }
                                        finally
                                        {
                                            clients.Remove(author_addr);
                                            author.conn.Close();
                                        }
                                    }
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
