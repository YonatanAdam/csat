using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Utils;

namespace csat;

public class ChatServer
{
    private readonly string _address;
    private readonly int _port;
    private readonly string _token;
    private TcpListener? _listener;
    private readonly Channel<Message> _messageChannel;
    private readonly MessageHandler _messageHandler;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger _logger;

    public ChatServer(string address, int port, ILogger logger)
    {
        _address = address;
        _port = port;
        _token = GenerateToken();
        _messageChannel = Channel.CreateUnbounded<Message>();
        _messageHandler = new MessageHandler(_token, logger);
        _cts = new CancellationTokenSource();
        _logger = logger;
    }

    public async Task StartAsync()
    {
        _logger.Info($"Token: {_token}");

        _listener = new TcpListener(IPAddress.Parse(Global.ADDRESS), Global.PORT);
        _listener.Start();
        _logger.Info($"Listening on {Global.ADDRESS}:{Global.PORT}...");

        var messageProcessingTask = ProcessMessagesAsync(_cts.Token);
        var consoleListenerTask = RunConsoleListenerAsync(_cts.Token);
        var clientAcceptTask = AcceptClientsAsync(_cts.Token);

        await Task.WhenAll(messageProcessingTask, consoleListenerTask, clientAcceptTask);
    }

    private async Task RunConsoleListenerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? input = await Task.Run(() => Console.ReadLine());
            if (!string.IsNullOrEmpty(input) && CommandParser.TryParse(input, out string cmd, out string[] args))
            {
                await _messageChannel.Writer.WriteAsync(new Message.AdminCommand(cmd, args));
            }
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken ct)
    {
        await foreach (var msg in _messageChannel.Reader.ReadAllAsync(ct))
        {
            await _messageHandler.HandleMessageAsync(msg, ct);
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener?.AcceptTcpClientAsync()!;
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var authorAddr = client.Client.RemoteEndPoint?.ToString();
        if (authorAddr == null)
        {
            _logger.Error("could not resolve client address");
            return;
        }

        var stream = client.GetStream();
        await _messageChannel.Writer.WriteAsync(new Message.ClientConnected(client), ct);

        Byte[] buff = new Byte[64];

        while (!ct.IsCancellationRequested && client.Connected)
        {
            try
            {
                var n = await stream.ReadAsync(buff, ct);
                if (n > 0)
                {
                    await _messageChannel.Writer.WriteAsync(new Message.NewMessage(authorAddr, buff[..n]), ct);
                }
                else
                {
                    await _messageChannel.Writer.WriteAsync(new Message.ClientDisconnected(authorAddr), ct);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info($"Read operation for {authorAddr} canceled.");
                break;
            }
            catch (IOException)
            {
                if (!client.Connected)
                    _logger.Info($"Connection for {authorAddr} closed by server.");
                else
                    _logger.Error($"Connection lost for {authorAddr}");
            }
            catch (Exception ex)
            {
                _logger.Error($"{ex.AsSensitive()}");
                break;
            }
        }

        client.Close();
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener?.Stop();
    }

    private string GenerateToken()
    {
        byte[] buffer = new byte[16];
        new Random().NextBytes(buffer);
        var token = Convert.ToHexString(buffer);
        return token;
    }
}