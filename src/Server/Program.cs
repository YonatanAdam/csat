using System.Net.Sockets;
using System.Net;
using System.Threading.Channels;
using csat;
using System.Text;
using System.Text.Unicode;

public static class Global
{
    public static bool SAFE_MODE = false;
    public static int port = 4293;
    public static string address = "127.0.0.1";
    public static TimeSpan BAN_LIMIT = TimeSpan.FromMinutes(10);
    public static TimeSpan MESSAGE_RATE = TimeSpan.FromSeconds(1);
    public static int STRIKE_LIMIT = 10;
    public static int MAX_USERNAME_LENGTH = 32;
}

public class Sensitive<T>
{
    public T Inner;

    public Sensitive(T inner) => Inner = inner;

    public override string ToString() => Global.SAFE_MODE ? "[REDACTED]" : $"{this.Inner}";
}

public static class SensitiveExtensions
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

    public static bool isValidUTF8(byte[] bytes) => Utf8.IsValid(bytes);


    public static bool ContainsEscapeSequences(string text)
    {
        foreach (char c in text)
        {
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
            {
                return true;
            }
            if (c == 0x7F)
            {
                return true;
            }
        }
        return false;
    }

    public static async Task HandleClientAsync(TcpClient client, ChannelWriter<Message> messages, CancellationToken ct = default)
    {
        var author_addr = client.Client.RemoteEndPoint?.ToString();
        if (author_addr == null)
        {
            Console.WriteLine("Error: could not resolve client address");
            return;
        }

        var stream = client.GetStream();
        
        await messages.WriteAsync(new Message.ClientConnected(client), ct);
        
        Byte[] buff = new Byte[64];

        while (!ct.IsCancellationRequested && client.Connected)
        {
            try
            {
                var n = await stream.ReadAsync(buff, ct);
                if (n > 0)
                {

                    await messages.WriteAsync(new Message.NewMessage(author_addr, buff[..n]), ct);

                }
                else
                {
                    // Client disconnected
                    await messages.WriteAsync(new Message.ClientDisconnected(author_addr), ct);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"INFO: Read operation for {author_addr} canceled.");
                break;
            }
            catch (IOException ex) when (ex.InnerException is SocketException sx)
            {
                if (sx.SocketErrorCode == SocketError.ConnectionAborted)
                    Console.WriteLine($"INFO: Connection closed for {author_addr} (likely banned)");
                else
                    Console.WriteLine($"Network Error: {sx.SocketErrorCode.AsSensitive()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.AsSensitive()}");
                break;
            }
        }
        
        client.Close();
    }

    public static async Task server(ChannelReader<Message> messages, string token, CancellationToken ct = default)
    {
        var clients = new Dictionary<string, Client>();
        var banned = new Dictionary<string, DateTime>();
        
            await foreach (var msg in messages.ReadAllAsync(ct))
            {
                switch (msg)
                {
                    case Message.ClientConnected(var author):
                        {
                            var author_addr = author.Client.RemoteEndPoint!.ToString();
                            var now = DateTime.UtcNow;
                            var stream = author.GetStream();
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
                                        await stream.WriteAsync(Encoding.UTF8.GetBytes(ban_msg), ct);
                                        banned.Add(author_addr!, now);
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

                            Console.WriteLine($"INFO: Client {author_addr} connected");
                            clients[author_addr] = new Client(author, now, 0, false);

                            try
                            {

                                await stream.WriteAsync(Encoding.UTF8.GetBytes("Token: "), ct);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Error: could not sent token prompt to {author_addr.AsSensitive()}: {e.AsSensitive()}");
                            }
                            break;
                        }
                    case Message.ClientDisconnected(var author_addr):
                        {
                            Console.WriteLine($"INFO: Client {author_addr} disconnected");
                            clients.Remove(author_addr);
                            break;
                        }
                    case Message.NewMessage(var author_addr, var bytes):
                        {
                            var text = Encoding.UTF8.GetString(bytes);
                            if (clients.TryGetValue(author_addr!, out var author))
                            {
                                var stream = author.conn.GetStream();
                                var now = DateTime.UtcNow;
                                var diff = now - author.last_message;

                                if (diff >= Global.MESSAGE_RATE)
                                {
                                    if (isValidUTF8(bytes) && !string.IsNullOrWhiteSpace(text) && !ContainsEscapeSequences(text))
                                    {
                                        author.last_message = now;
                                        
                                        if (author.authenticated)
                                        {
                                            // Check if username is set - if not, this is the username input
                                            if (author.Username == "Unknown")
                                            {
                                                var username = text.Trim();
                                                
                                                // Validate username
                                                if (username.Length == 0 || username.Length > Global.MAX_USERNAME_LENGTH)
                                                {
                                                    try
                                                    {
                                                        await stream.WriteAsync(Encoding.UTF8.GetBytes($"Username must be between 1 and {Global.MAX_USERNAME_LENGTH} characters. Try again: "), ct);
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        Console.WriteLine($"Error: Could not send username validation error to {author_addr}: {e.AsSensitive()}");
                                                    }
                                                }
                                                else if (clients.Values.Any(c => c.Username == username && c.authenticated && c.conn.Connected))
                                                {
                                                    try
                                                    {
                                                        await stream.WriteAsync(Encoding.UTF8.GetBytes("Username already taken. Try again: "), ct);
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        Console.WriteLine($"Error: Could not send username taken error to {author_addr}: {e.AsSensitive()}");
                                                    }
                                                }
                                                else
                                                {
                                                    author.Username = username;
                                                    Console.WriteLine($"INFO: {author_addr} set username to '{username}'");
                                                    
                                                    try
                                                    {
                                                        await stream.WriteAsync(Encoding.UTF8.GetBytes($"Welcome to the club, {username}!\n"), ct);
                                                        
                                                        // Notify other users
                                                        var joinMsg = Encoding.UTF8.GetBytes($"[{username} joined the chat]\n");
                                                        foreach (var (addr, client) in clients)
                                                        {
                                                            if (addr != author_addr && client.authenticated && client.Username != "Unknown")
                                                            {
                                                                try
                                                                {
                                                                    if (client.conn.Connected)
                                                                        await client.conn.GetStream().WriteAsync(joinMsg, ct);
                                                                }
                                                                catch (Exception e)
                                                                {
                                                                    Console.WriteLine($"Error: could not broadcast join message to {addr}: {e}");
                                                                }
                                                            }
                                                        }
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        Console.WriteLine($"Error: Could not send final welcome message to {author_addr}: {e.AsSensitive()}");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // User has username set, this is a regular chat message
                                                Console.WriteLine($"INFO: [{author.Username}] {author_addr} sent message: [{string.Join(", ", bytes)}]");
                                                
                                                var messageWithUsername = Encoding.UTF8.GetBytes($"[{author.Username}]{text}"); // [username]content
                                                
                                                foreach (var (addr, client) in clients)
                                                {
                                                    if (addr != author_addr && client.authenticated && client.Username != "Unknown")
                                                    {
                                                        try
                                                        {
                                                            if (client.conn.Connected)
                                                                await client.conn.GetStream().WriteAsync(messageWithUsername, ct);
                                                        }
                                                        catch (Exception e)
                                                        {
                                                            Console.WriteLine($"Error: could not broadcast message to all the clients from {author_addr}: {e}");
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Token authentication
                                            if (text.TrimEnd() == token)
                                            {
                                                author.authenticated = true;
                                                try
                                                {
                                                    await stream.WriteAsync(Encoding.UTF8.GetBytes("Token accepted! Enter your username: "), ct);
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"Error: Could not send username prompt to {author_addr}: {e.AsSensitive()}");
                                                }
                                                Console.WriteLine($"INFO: {author_addr} authenticated!");

                                            }
                                            else
                                            {
                                                try
                                                {
                                                    Console.WriteLine($"INFO: {author_addr.AsSensitive()} failed authentication!");
                                                    await stream.WriteAsync(Encoding.UTF8.GetBytes("Invalid Token!\n"), ct);
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"Error: could not notify client {author_addr.AsSensitive()} about invalid token: {e.AsSensitive()}");
                                                }
                                                author.conn.Close();
                                                clients.Remove(author_addr);
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
                                                await author.conn.GetStream().WriteAsync(ban_msg, ct);
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

                                            await author.conn.GetStream().WriteAsync(ban_msg, ct);
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

    static async Task Main(string[] args)
    {
        byte[] buffer = new byte[16];
        var rnd = new Random();
        rnd.NextBytes(buffer);
        var token = Convert.ToHexString(buffer);
        Console.WriteLine($"Token: {token}");

        TcpListener? listener = null;
        try
        {
            IPAddress address = IPAddress.Parse(Global.address);
            listener = new TcpListener(address, Global.port);
            listener.Start();
            Console.WriteLine($"INFO: Listening on {Global.address.AsSensitive()}:{Global.port.AsSensitive()}...");

            var c = Channel.CreateUnbounded<Message>();
            var (message_sender, message_receiver) = (c.Writer, c.Reader);

            var serverTask = server(message_receiver, token);

            using var cts = new CancellationTokenSource();
            var ct = cts.Token;

            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);

                _ = HandleClientAsync(client, message_sender, ct);
            }

            await serverTask;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("INFO: Server shutdown requested.");

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
            listener?.Stop();
        }

    }
}