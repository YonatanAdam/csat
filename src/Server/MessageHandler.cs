using System.Net.Sockets;
using System.Text;
using Utils;

namespace csat;

public class MessageHandler
{
    private readonly Dictionary<string, Client> _clients;
    private readonly Dictionary<string, DateTime> _banned;
    private readonly string _token;
    private readonly AdminCommandHandler _adminCommandHandler;
    private readonly ILogger _logger;

    public MessageHandler(string token, ILogger logger)
    {
        this._token = token;
        this._clients = new Dictionary<string, Client>();
        this._banned = new Dictionary<string, DateTime>();
        this._adminCommandHandler = new AdminCommandHandler(_clients, _banned);
        this._logger = logger;
    }

    public async Task HandleMessageAsync(Message msg, CancellationToken ct)
    {
        switch (msg)
        {
            case Message.ClientConnected c:
                await HandleClientConnectedAsync(c, ct);
                break;
            case Message.ClientDisconnected d:
                await HandleClientDisconnectedAsync(d, ct);
                break;
            case Message.NewMessage m:
                await HandleNewMessageAsync(m, ct);
                break;
            case Message.AdminCommand a:
                await _adminCommandHandler.HandleCommandAsync(a, ct);
                break;
        }
    }
    
    private async Task HandleNewMessageAsync(Message.NewMessage msg, CancellationToken ct)
    {
        var text = Encoding.UTF8.GetString(msg.Data);
        if (!this._clients.TryGetValue(msg.author_addr!, out var client))
            return;

        var stream = client.conn.GetStream();
        var curNow = DateTime.UtcNow;

        if (!client.CanSendMessage(curNow))
        {
            await HandleRateLimitViolationAsync(msg.author_addr, client, curNow, ct);
            return;
        }

        if (!MessageValidator.IsValidMessage(msg.Data, text))
        {
            await HandleInvalidMessageAsync(msg.author_addr, client, curNow, ct);
            return;
        }

        client.last_message = curNow;

        if (!client.authenticated)
        {
            await HandleAuthenticationAsync(msg.author_addr, client, text, stream, ct);
        }
        else if (client.Username == "Unknown")
        {
            await HandleUsernameSetupAsync(client, text, stream, ct);
        }
        else
        {
            await BroadcastChatMessageAsync(msg.author_addr, client, text, msg.Data, ct);
        }
    }

    private async Task BroadcastChatMessageAsync(string senderAddr, Client sender, string text, byte[] data,
        CancellationToken ct)
    {
        _logger.Info(
            $"[{sender.Username}] {sender.conn.Client.RemoteEndPoint} sent message: [{string.Join(", ", data)}]");

        var chatMsg = Encoding.UTF8.GetBytes($"[{sender.Username}]{text}");

        foreach (var (addr, client) in _clients)
        {
            if (addr != senderAddr && client.authenticated)
            {
                try
                {
                    await client.conn.GetStream().WriteAsync(chatMsg, ct);
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleUsernameSetupAsync(Client client, string text, NetworkStream stream, CancellationToken ct)
    {
        var username = text.Trim();

        if (username.Length == 0 || username.Length > Global.MAX_USERNAME_LENGTH)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("Invalid length. Try again: "), ct);
        }
        else if (_clients.Values.Any(c => c.Username == username))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("Taken. Try again: "), ct);
        }
        else
        {
            client.Username = username;
            _logger.Info($"{client.conn.Client.RemoteEndPoint} is now '{username}'");
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"[Server]Welcome {username}!\n"), ct);
        }
    }

    private async Task HandleAuthenticationAsync(string addr, Client client, string text, NetworkStream stream,
        CancellationToken ct)
    {
        if (text.TrimEnd() == _token)
        {
            client.authenticated = true;
            _logger.Info($"{client.conn.Client.RemoteEndPoint} is now authenticated");
            await stream.WriteAsync(Encoding.UTF8.GetBytes("Authenticated! Enter username: "), ct);
        }
        else
        {
            client.conn.Close();
            _clients.Remove(addr);
        }
    }

    private async Task HandleInvalidMessageAsync(string addr, Client client, DateTime now, CancellationToken ct)
    {
        client.Strike();
        if (client.ShouldBeBanned())
        {
            await BanClientAsync(addr, client, now, ct);
        }
    }

    private async Task HandleRateLimitViolationAsync(string addr, Client client, DateTime now, CancellationToken ct)
    {
        client.Strike();
        if (client.ShouldBeBanned())
        {
            await BanClientAsync(addr, client, now, ct);
        }
    }

    private async Task BanClientAsync(string addr, Client client, DateTime now, CancellationToken ct)
    {
        _logger.Info($"Client {addr} got banned");
        _banned[addr] = now;
        var banMsg = Encoding.ASCII.GetBytes("[Server]You are banned MF\n");

        try
        {
            await client.conn.GetStream().WriteAsync(banMsg, ct);
        }
        catch (Exception e)
        {
            _logger.Error($"could not send banned msg to {addr}: {e}");
        }
        finally
        {
            _clients.Remove(addr);
            client.conn.Close();
        }
    }

    private Task HandleClientDisconnectedAsync(Message.ClientDisconnected addr, CancellationToken ct)
    {
        _logger.Info($"Client {addr.author_addr} disconnected");
        this._clients.Remove(addr.author_addr);
        return Task.CompletedTask;
    }

    private async Task HandleClientConnectedAsync(Message.ClientConnected author, CancellationToken ct)
    {
        var author_addr = author.client.Client.RemoteEndPoint?.ToString();
        var now = DateTime.UtcNow;
        var stream = author.client.GetStream();
        if (author_addr != null && this._banned.Remove(author_addr, out DateTime banned_at))
        {
            var diff = now - banned_at;
            if (diff < Global.BAN_LIMIT)
            {
                var timeLeft = Global.BAN_LIMIT - diff;
                _logger.Warning($"{author_addr} blocked (Banned for {timeLeft.TotalSeconds:F0}s)");
                string ban_msg = $"You are banned MF: {timeLeft.TotalSeconds:F0} secs left\n";

                try
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(ban_msg), ct);
                    this._banned.Add(author_addr, now);
                }
                catch
                {
                }
                finally
                {
                    author.client.Close();
                }

                return;
            }
        }

        _logger.Info($"Client {author_addr} connected");
        if (author_addr != null) this._clients[author_addr] = new Client(author.client, now, 0, false);
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("[Server]Enter auth token: "), ct);
        }
        catch
        {
        }
    }
}