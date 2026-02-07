using System.Text;
using Utils;

namespace csat;

public class AdminCommandHandler {
    private readonly Dictionary<string, Client> _clients;
    private readonly Dictionary<string, DateTime> _banned;
    private readonly ILogger _logger;
    private readonly string _token;

    public AdminCommandHandler(Dictionary<string, Client> clients, Dictionary<string, DateTime> banned, string token, ILogger logger)
    {
        _clients = clients;
        _banned = banned;
        _logger = logger;
        _token = token;
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
            case "token":
            {
                await HandleTokenCommand(ct);
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
        foreach (var c in _clients.Values.Where(x => x.Authenticated))
            await c.Conn.GetStream().WriteAsync(shutData, ct);
        Environment.Exit(0);
    }

    private async Task HandleTokenCommand(CancellationToken ct)
    {
        _logger.Info($"Token: {_token}");
    }

    private Task HandleUserCommandAsync()
    {
        if (_clients.Count == 0)
        {
            _logger.Info("No clients found");
            return Task.CompletedTask;
        }

        foreach (var client in _clients.Values.Where(c => c.Authenticated && c.Username != null))
            Console.WriteLine($"- {client.Username} ({client.Conn.Client.RemoteEndPoint!})");

        return Task.CompletedTask;
    }

    private async Task HandleKickCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0) Console.WriteLine("Usage: /kick <client(s)>");
        foreach (var name in args)
        {
            var target = _clients.FirstOrDefault(c => c.Value.Username == name);
            if (target.Value != null)
            {
                var data = Encoding.UTF8.GetBytes($"[Server]Admin kicked you!\n");
                try
                {
                    await target.Value.Conn.GetStream().WriteAsync(data, ct);
                }
                catch
                {
                    _logger.Warning($"could not send kick message to {target.Value.Conn.Client.RemoteEndPoint}");
                }

                target.Value.Conn.Close();
                _clients.Remove(target.Key);
                _logger.Info($"Kicked {name} ({target.Key})");
            }
            else _logger.Warning($"could not find client '{name}'");
        }
    }

    private async Task HandleBroadcastCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0) Console.WriteLine("Usage: /broadcast <msg>");
        var bText = string.Join(" ", args);
        var bData = Encoding.UTF8.GetBytes($"[Server]{bText}\n");
        foreach (var c in _clients.Values.Where(x => x.Authenticated))
            await c.Conn.GetStream().WriteAsync(bData, ct);
    }

    private async Task HandleKickAllCommand(CancellationToken ct)
    {
        foreach (var client in _clients.Values.ToList())
        {
            var data = Encoding.UTF8.GetBytes($"[Server]Admin kicked you! (and everybody else)\n");
            try
            {
                await client.Conn.GetStream().WriteAsync(data, ct);
            }
            catch
            {
                _logger.Warning($"could not send kick message to {client.Conn.Client.RemoteEndPoint}");
            }

            client.Conn.Close();
        }

        _clients.Clear();
        _logger.Info("Successfully kicked all clients");
    }

    private async Task HandleMessageCommandAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: /msg <client> <msg>");
            return;
        }

        var target = _clients.Values.FirstOrDefault(x => x.Username == args[0]);
        if (target == null)
        {
            _logger.Warning($"Client '{args[0]}' does not exist");
            return;
        }

        var mData = Encoding.UTF8.GetBytes(
            $"[Server (private)]{string.Join(" ", args[1..])}\n");

        try
        {
            await target.Conn.GetStream().WriteAsync(mData, ct);
        }
        catch
        {
            _logger.Warning($"could not send message to {target.Conn.Client.RemoteEndPoint}");
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
            _logger.Error($"Client '{args[0]}' does not exist");
            return;
        }

        var addr = target.Conn.Client.RemoteEndPoint?.ToString();
        if (addr == null)
        {
            _logger.Error("Could not get client address");
            return;
        }

        var reason = args.Length > 1 ? string.Join(" ", args[1..]) : "No reason provided";
        var banMsg = Encoding.UTF8.GetBytes($"[Server]You are banned MF, reason: {reason}\n");

        try
        {
            await target.Conn.GetStream().WriteAsync(banMsg, ct);
        }
        catch
        {
            _logger.Warning($"could not send ban message to {target.Conn.Client.RemoteEndPoint}");
        }

        _banned[addr] = DateTime.UtcNow;
        _clients.Remove(addr);
        target.Conn.Close();

        _logger.Info($"Banned {args[0]} ({addr}). Reason: {reason}");
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
                          "  /help /h             Print this help message");
    }
}