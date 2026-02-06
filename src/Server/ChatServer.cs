using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Utils;

namespace csat;

public class ChatServer
{
    private readonly string _address;
    private readonly int _port;
    private readonly string _token;
    private TcpListener? _listener;
    private Channel<Message> _messageChannel;
    private MessageHandler _messageHandler;
    private CancellationTokenSource _cts;

    public ChatServer(string address, int port)
    {
        _address = address;
        _port = port;
        _token = GenerateToken();
        _messageChannel = Channel.CreateUnbounded<Message>();
        _messageHandler = new MessageHandler(this._token);
        _cts = new CancellationTokenSource();
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"Token: {this._token}");
        
        this._listener = new TcpListener(IPAddress.Parse(Global.ADDRESS), Global.PORT);
        this._listener.Start();
        Console.WriteLine($"INFO: Listening on {Global.ADDRESS}:{Global.PORT}...");

        var messageProcessingTask = ProcessMessagesAsync(this._cts.Token);
        var consoleListenerTask = RunConsoleListenerAsync(this._cts.Token);
        var clientAcceptTask = AcceptClientsAsync(this._cts.Token);
        
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
                TcpClient client = await this._listener?.AcceptTcpClientAsync()!;
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
        var author_addr = client.Client.RemoteEndPoint?.ToString();
        if (author_addr == null)
        {
            Console.WriteLine("Error: could not resolve client address");
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
                    await _messageChannel.Writer.WriteAsync(new Message.NewMessage(author_addr, buff[..n]), ct);
                }
                else
                {
                    await _messageChannel.Writer.WriteAsync(new Message.ClientDisconnected(author_addr), ct);
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

    public void Stop()
    {
        this._cts.Cancel();
        this._listener?.Stop();
    }

    private string GenerateToken()
    {
        byte[] buffer = new byte[16];
        new Random().NextBytes(buffer);
        var token = Convert.ToHexString(buffer);
        return token;
    }
}












