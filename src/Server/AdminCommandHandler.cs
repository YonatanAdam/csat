using System.Text;

namespace csat;

public class AdminCommandHandler
{
    private readonly Dictionary<string, Client> _clients;
    private readonly Dictionary<string, DateTime> _banned;

    public AdminCommandHandler(Dictionary<string, Client> clients, Dictionary<string, DateTime> banned)
    {
        this._clients = clients;
        this._banned = banned;
    }
    
    public async Task HandleCommandAsync(Message.AdminCommand msg, CancellationToken ct)
    {
        switch (msg.command)
        {
            case "users":
                await HandleUserCommandAsync();
                break;

            case "shutdown":
                await HandleShutdownCommandAsync(ct);
                break;

            case "kick":
                await HandleKickCommandAsync(msg.args, ct);
                break;

            case "kickall":
                await HandleKickAllCommand(ct);
                break;

            case "broadcast":
                await HandleBroadcastCommandAsync(msg.args, ct);
                break;

            case "msg":
                await HandleMessageCommandAsync(msg.args, ct);
                break;

            case "ban":
            {
                await HandleBanCommand(msg.args, ct);
                break;
            }

            case "help" or "h":
                HandleHelp();
                break;

            default:
                Console.WriteLine($"Unknown command: '{msg.command}'");
                break;
        }
    }

    private async Task HandleShutdownCommandAsync(CancellationToken ct)
    {
        var shutData = Encoding.UTF8.GetBytes($"[Server]Shutting down\n");
        foreach (var c in this._clients.Values.Where(x => x.authenticated))
            await c.conn.GetStream().WriteAsync(shutData, ct);
        Environment.Exit(0);
    }

    private Task HandleUserCommandAsync()
    {
        if (this._clients.Count == 0)
        {
            Console.WriteLine("No clients found");
            return Task.CompletedTask;
        }

        foreach (var client in this._clients.Values.Where(c => c.authenticated && c.Username != null))
            Console.WriteLine($"- {client.Username} ({client.conn.Client.RemoteEndPoint!.ToString()})");

        return Task.CompletedTask;
    }

    private async Task HandleKickCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0) Console.WriteLine("Usage: /kick <client(s)>");
        foreach (var name in args)
        {
            var target = this._clients.FirstOrDefault(c => c.Value.Username == name);
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
                this._clients.Remove(target.Key);
                Console.WriteLine($"INFO: Kicked {name} ({target.Key})");
            }
            else Console.WriteLine($"Error: could not find client '{name}'");
        }
    }

    private async Task HandleBroadcastCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0) Console.WriteLine("Usage: /broadcast <msg>");
        var bText = string.Join(" ", args);
        var bData = Encoding.UTF8.GetBytes($"[Server]{bText}\n");
        foreach (var c in this._clients.Values.Where(x => x.authenticated))
            await c.conn.GetStream().WriteAsync(bData, ct);
    }

    private async Task HandleKickAllCommand(CancellationToken ct)
    {
        foreach (var client in this._clients.Values.ToList())
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

        this._clients.Clear();
        Console.WriteLine("Successfully kicked all clients");
    }

    private async Task HandleMessageCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: /msg <client> <msg>");
            return;
        }

        var target = this._clients.Values.FirstOrDefault(x => x.Username == args[0]);
        if (target == null)
        {
            Console.WriteLine($"Error: Client '{args[0]}' does not exist");
            return;
        }

        var mData = Encoding.UTF8.GetBytes(
            $"[Server (private)]{string.Join(" ", args[1..])}\n");

        try
        {
            await target.conn.GetStream().WriteAsync(mData, ct);
        }
        catch
        {
        }
    }

    private async Task HandleBanCommand(string[] args, CancellationToken ct)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: /ban <client> [reason]");
            return;
        }

        var target = _clients.Values.FirstOrDefault(x => x.Username == args[0]);

        if (target == null)
        {
            Console.WriteLine($"Error: Client '{args[0]}' does not exist");
            return;
        }

        var addr = target.conn.Client.RemoteEndPoint?.ToString();
        if (addr == null)
        {
            Console.WriteLine("Error: Could not get client address");
            return;
        }

        var reason = args.Length > 1 ? string.Join(" ", args[1..]) : "No reason provided";
        var banMsg = Encoding.UTF8.GetBytes($"[Server]You are banned MF, reason: {reason}\n");

        try
        {
            await target.conn.GetStream().WriteAsync(banMsg, ct);
        }
        catch { }

        _banned[addr] = DateTime.UtcNow;
        _clients.Remove(addr);
        target.conn.Close();

        Console.WriteLine($"INFO: Banned {args[0]} ({addr}). Reason: {reason}");

    }
    
    private void HandleHelp()
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
}