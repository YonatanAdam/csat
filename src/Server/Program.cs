using System.Net.Sockets;
using System.Net;
using System.Threading.Channels;
using csat;
using System.Text;
using System.Text.Unicode;
using Utils;

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
    public record AdminCommand(string command, string[] args) : Message;
}

public class Program
{
    public static bool isValidUTF8(byte[] bytes) => Utf8.IsValid(bytes);

    public static bool ContainsEscapeSequences(string text)
    {
        foreach (char c in text)
        {
            if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') return true;
            if (c == 0x7F) return true;
        }

        return false;
    }

    public static async Task HandleClientAsync(TcpClient client, ChannelWriter<Message> messages,
        CancellationToken ct = default)
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
                    await messages.WriteAsync(new Message.ClientDisconnected(author_addr), ct);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"INFO: Read operation for {author_addr} canceled.");
                break;
            }
            catch (IOException)
            {
                if (!client.Connected)
                    Console.WriteLine($"INFO: Connection for {author_addr} closed by server.");
                else
                    Console.WriteLine($"Network Error: Connection lost for {author_addr}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.AsSensitive()}");
                break;
            }
        }

        client.Close();
    }

    private static void HandleHelp()
    {
        Console.WriteLine("Available commands:\n" +
                          "  /users               List all connected users\n" +
                          "  /shutdown            Shutdown the server\n" +
                          "  /kick <client>       Kick a client from the server\n" +
                          "  /kickall             Kicks all clients from the server\n" +
                          "  /broadcast <msg>     Send a message to all clients\n" +
                          "  /msg <client> <msg>  Send a message to a specified client\n" +
                          "  /help /h             Show this help message");
    }

    private static async Task ProcessAdminCommand(string command, string[] args, Dictionary<string, Client> clients,
        CancellationToken ct)
    {
        switch (command)
        {
            case "users":
                if (clients.Count == 0)
                {
                    Console.WriteLine("No clients found");
                    break;
                }
                foreach (var client in clients.Values.Where(c => c.authenticated && c.Username != null))
                    Console.WriteLine($"- {client.Username} ({client.conn.Client.RemoteEndPoint!.ToString()})");
                break;

            case "shutdown":
                var shutData = Encoding.UTF8.GetBytes($"[Server]Shutting down\n");
                foreach (var c in clients.Values.Where(x => x.authenticated))
                    await c.conn.GetStream().WriteAsync(shutData, ct);
                Environment.Exit(0);
                break;

            case "kick":
                if (args.Length == 0) Console.WriteLine("Usage: /kick <client(s)>");
                foreach (var name in args)
                {
                    var target = clients.FirstOrDefault(c => c.Value.Username == name);
                    if (target.Value != null)
                    {
                        var data = Encoding.UTF8.GetBytes($"[Server]Admin kicked you!\n");
                        try
                        {
                            await target.Value.conn.GetStream().WriteAsync(data, ct);
                        }
                        catch
                        {
                        }

                        target.Value.conn.Close();
                        clients.Remove(target.Key);
                        Console.WriteLine($"INFO: Kicked {name} ({target.Key})");
                    }
                    else Console.WriteLine($"Error: could not find client '{name}'");
                }

                break;

            case "kickall":
                foreach (var client in clients.Values.ToList())
                {
                    var data = Encoding.UTF8.GetBytes($"[Server]Admin kicked you! (and everybody else)\n");
                    try
                    {
                        await client.conn.GetStream().WriteAsync(data, ct);
                    }
                    catch
                    {
                    }

                    client.conn.Close();
                }

                clients.Clear();
                Console.WriteLine("Successfully kicked all clients");
                break;

            case "broadcast":
                if (args.Length == 0) Console.WriteLine("Usage: /broadcast <msg>");
                var bText = string.Join(" ", args);
                var bData = Encoding.UTF8.GetBytes($"[Server]{bText}\n");
                foreach (var c in clients.Values.Where(x => x.authenticated))
                    await c.conn.GetStream().WriteAsync(bData, ct);
                break;

            case "msg":
                if (args.Length < 2) Console.WriteLine("Usage: /msg <client> <msg>");
                else
                {
                    var target = clients.Values.FirstOrDefault(x => x.Username == args[0]);
                    if (target == null) Console.WriteLine($"Error: Client '{args[0]}' does not exist");
                    else
                    {
                        var mData = Encoding.UTF8.GetBytes($"[Server (private)]{string.Join(" ", args[1..])}\n");
                        await target.conn.GetStream().WriteAsync(mData, ct);
                    }
                }

                break;

            case "ban":
            {
                if (args.Length < 1) Console.WriteLine("Usage: /ban <client> <msg>(optional)");
                else
                {
                    var target = clients.Values.FirstOrDefault(x => x.Username == args[0]);
                    if (target == null) Console.WriteLine($"Error: Client '{args[0]}' does not exist");
                    else
                    {
                        var mData = Encoding.UTF8.GetBytes($"[Server (private)]You are banned MF, reason: {string.Join(" ", args[1..])}\n");
                        throw new Exception("TODO: ban user");
                    }
                }
                
            } break;

            case "help" or "h":
                HandleHelp();
                break;

            default:
                Console.WriteLine($"Unknown command: '{command}'");
                break;
        }
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
                    var author_addr = author.Client.RemoteEndPoint.ToString();
                    var now = DateTime.UtcNow;
                    var stream = author.GetStream();
                    if (banned.Remove(author_addr!, out DateTime banned_at))
                    {
                        var diff = now - banned_at;
                        if (diff < Global.BAN_LIMIT)
                        {
                            var timeLeft = Global.BAN_LIMIT - diff;
                            Console.WriteLine($"INFO: {author_addr} blocked (Banned for {timeLeft.TotalSeconds:F0}s)");
                            string ban_msg = $"You are banned MF: {timeLeft.TotalSeconds:F0} secs left\n";

                            try
                            {
                                await stream.WriteAsync(Encoding.UTF8.GetBytes(ban_msg), ct);
                                banned.Add(author_addr, now);
                            }
                            catch
                            {
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
                    catch
                    {
                    }

                    break;

                case Message.ClientDisconnected(var addr):
                    Console.WriteLine($"INFO: Client {addr} disconnected");
                    clients.Remove(addr);
                    break;

                case Message.NewMessage(var addr, var bytes):
                    var text = Encoding.UTF8.GetString(bytes);
                    if (clients.TryGetValue(addr!, out var clientObj))
                    {
                        var s = clientObj.conn.GetStream();
                        var curNow = DateTime.UtcNow;
                        if ((curNow - clientObj.last_message) >= Global.MESSAGE_RATE)
                        {
                            if (isValidUTF8(bytes) && !string.IsNullOrWhiteSpace(text) &&
                                !ContainsEscapeSequences(text))
                            {
                                clientObj.last_message = curNow;
                                if (clientObj.authenticated)
                                {
                                    if (clientObj.Username == "Unknown")
                                    {
                                        var username = text.Trim();
                                        if (username.Length == 0 || username.Length > Global.MAX_USERNAME_LENGTH)
                                            await s.WriteAsync(Encoding.UTF8.GetBytes("Invalid length. Try again: "),
                                                ct);
                                        else if (clients.Values.Any(c => c.Username == username))
                                            await s.WriteAsync(Encoding.UTF8.GetBytes("Taken. Try again: "), ct);
                                        else
                                        {
                                            clientObj.Username = username;
                                            Console.WriteLine($"INFO: {addr} is now '{username}'");
                                            await s.WriteAsync(Encoding.UTF8.GetBytes($"Welcome {username}!\n"), ct);
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine(
                                            $"INFO: [{clientObj.Username}] {clientObj.conn.Client.RemoteEndPoint.ToString()} sent message: [{string.Join(", ", bytes)}]");

                                        var chatMsg = Encoding.UTF8.GetBytes($"[{clientObj.Username}]{text}");
                                        foreach (var (cAddr, cTarget) in clients)
                                            if (cAddr != addr && cTarget.authenticated)
                                                await cTarget.conn.GetStream().WriteAsync(chatMsg, ct);
                                    }
                                }
                                else if (text.TrimEnd() == token)
                                {
                                    clientObj.authenticated = true;
                                    await s.WriteAsync(Encoding.UTF8.GetBytes("Authenticated! Enter username: "), ct);
                                }
                                else
                                {
                                    clientObj.conn.Close();
                                    clients.Remove(addr);
                                }
                            }
                            else
                            {
                                clientObj.strike_count += 1;
                                if (clientObj.strike_count >= Global.STRIKE_LIMIT)
                                {
                                    Console.WriteLine($"INFO: Client {addr} got banned");
                                    banned[addr] = curNow;
                                    var banMsg = Encoding.ASCII.GetBytes("[Server]You are banned MF\n");
                                    try
                                    {
                                        await clientObj.conn.GetStream().WriteAsync(banMsg, ct);
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine($"Error: could not send banned msg to {addr}: {e}");
                                    }
                                    finally
                                    {
                                        clients.Remove(addr);
                                        clientObj.conn.Close(); 
                                    }
                                }
                            }
                        }
                        else
                        {
                            clientObj.strike_count += 1;
                            if (clientObj.strike_count >= Global.STRIKE_LIMIT)
                            {
                                Console.WriteLine($"INFO: Client {addr} got banned");
                                banned[addr] = curNow;
                                var banMsg = Encoding.ASCII.GetBytes("[Server]You are banned MF\n");
                                try
                                {
                                    await clientObj.conn.GetStream().WriteAsync(banMsg, ct);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Error: could not send banned msg to {addr}: {e}");
                                }
                                finally
                                {
                                    clients.Remove(addr);
                                    clientObj.conn.Close(); 
                                }
                            }
                        }
                    }

                    break;

                case Message.AdminCommand(string cmd, string[] args):
                    await ProcessAdminCommand(cmd, args, clients, ct);
                    break;
            }
        }
    }

    static async Task Main(string[] args)
    {
        byte[] buffer = new byte[16];
        new Random().NextBytes(buffer);
        var token = Convert.ToHexString(buffer);
        Console.WriteLine($"Token: {token}");

        TcpListener listener = new TcpListener(IPAddress.Parse(Global.address), Global.port);
        listener.Start();
        Console.WriteLine($"INFO: Listening on {Global.address}:{Global.port}...");

        var chan = Channel.CreateUnbounded<Message>();
        var serverTask = server(chan.Reader, token);

        using var cts = new CancellationTokenSource();
        Task.Run(() => RunConsoleListener(cts.Token, chan.Writer));

        while (!cts.Token.IsCancellationRequested)
        {
            TcpClient client = await listener.AcceptTcpClientAsync(cts.Token);
            _ = HandleClientAsync(client, chan.Writer, cts.Token);
        }

        await serverTask;
    }

    private static async Task RunConsoleListener(CancellationToken ct, ChannelWriter<Message> writer)
    {
        while (!ct.IsCancellationRequested)
        {
            string? input = await Task.Run(() => Console.ReadLine());
            if (!string.IsNullOrEmpty(input) && CommandParser.TryParse(input, out string cmd, out string[] args))
            {
                await writer.WriteAsync(new Message.AdminCommand(cmd, args));
            }
        }
    }
}