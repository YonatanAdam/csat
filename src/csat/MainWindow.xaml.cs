using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Utils;

namespace csat;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Message> _history = new ObservableCollection<Message>();
    private readonly string[] _commands = ["connect", "disconnect", "help", "clear", "exit", "ping", "users"];
    private readonly List<string> _clientMessageHistory = new List<string>();
    private int _historyIndex = -1;
    private TcpClient? _client = null;
    private NetworkStream? _activeStream;
    private bool _isConnected;
    private readonly int _port = 4293;

    public MainWindow()
    {
        InitializeComponent();
        WelcomeMessage();

        TextPrompt.Focus();
        ChatHistory.ItemsSource = _history;

        _history.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Ensure scroll to the very last item added
                ChatHistory.ScrollIntoView(_history[^1]);
            }
        };
    }

    private void WelcomeMessage()
    {
        string msg = "You are offline. Use /connect <ip> to connect to a server";
        _history.Add(new Message(msg, "System"));
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SendAsync();
        }
        catch (Exception ex)
        {
            _history.Add(new Message($"Unexpected error: {ex.Message}", "System"));
        }
    }

    private async Task SendAsync()
    {
        var messageText = TextPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        _history.Add(new Message(messageText, "You"));
        _clientMessageHistory.Add(messageText);
        _historyIndex = -1;
        TextPrompt.Clear();

        if (messageText.StartsWith('/'))
        {
            HandleCommand(messageText);
            return;
        }

        if (_activeStream == null || !_isConnected)
            return;

        var bytes = Encoding.UTF8.GetBytes(messageText + "\n");
        await _activeStream.WriteAsync(bytes);

    }

    private void Connect(string ip, string token)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Dispatcher.BeginInvoke(() => _history.Add(new Message("Connecting to server...", "System")));

                _client = new TcpClient();
                await _client.ConnectAsync(ip, _port);
                _activeStream = _client.GetStream();
                _isConnected = true;

                await Dispatcher.BeginInvoke(() =>
                {
                    _history.Clear();
                    ConnectionLight.Fill = Brushes.Orange;
                });

                var tokenBytes = Encoding.UTF8.GetBytes(token + "\n");
                await _activeStream.WriteAsync(tokenBytes);

                var buffer = new byte[1024];
                while (_client.Connected && _isConnected)
                {
                    int n;
                    try
                    {
                        n = await _activeStream.ReadAsync(buffer);
                        if (n == 0) break; // Server disconnected
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    var receivedText = Encoding.UTF8.GetString(buffer, 0, n).TrimEnd();

                    await Dispatcher.BeginInvoke(() => 
                    {
                        string username = "Server";
                        string content = receivedText;

                        // Check if message starts with [username] format
                        if (receivedText.StartsWith("[") && receivedText.Contains("]"))
                        {
                            var parts = receivedText.Split(']');
                            username = parts[0][1..];
                            content = parts[1].TrimStart();
                        }

                        _history.Add(new Message(content, username));

                        if (receivedText.Contains("Welcome"))
                        {
                            ConnectionLight.Fill = Brushes.LimeGreen;
                        }
                    });
                }

            }
            catch (Exception e)
            {
                await Dispatcher.BeginInvoke(() => _history.Add(new Message($"Failed connecting to server: {e.Message}", "System")));
            }
            finally
            {
                _isConnected = false;
                _activeStream?.Dispose();
                _client?.Dispose();
                await Dispatcher.BeginInvoke(() => _history.Add(new Message("Disconnected from server", "System")));
                await Dispatcher.BeginInvoke(() => ConnectionLight.Fill = Brushes.Red);
            }
        });
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e) => _client?.Close();

    private void TextPrompt_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TextPrompt.Text.StartsWith('/') && e.Key == Key.Tab)
        {
            Autocomplete();
            e.Handled = true;
        }

        if (_clientMessageHistory.Count == 0) return;

        if ((e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.Up)
        {
            e.Handled = true;
            if (_historyIndex < _clientMessageHistory.Count - 1)
            {
                _historyIndex++;
            }
            else
            {
                // Wrap to the beginning (empty prompt)
                _historyIndex = -1;
            }

            if (_historyIndex == -1)
            {
                TextPrompt.Text = string.Empty;
            }
            else
            {
                TextPrompt.Text = _clientMessageHistory[^(_historyIndex + 1)];
                TextPrompt.CaretIndex = TextPrompt.Text.Length;
            }
        }
        // Ctrl+N for next (down in history)
        else if ((e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.Down)
        {
            e.Handled = true;
            if (_historyIndex > -1)
            {
                _historyIndex--;
            }
            else
            {
                // Wrap to the end (most recent message)
                _historyIndex = _clientMessageHistory.Count - 1;
            }

            if (_historyIndex == -1)
            {
                TextPrompt.Text = string.Empty;
            }
            else
            {
                TextPrompt.Text = _clientMessageHistory[^(_historyIndex + 1)];
                TextPrompt.CaretIndex = TextPrompt.Text.Length;
            }
        }
    }


    private void HandleCommand(string input)
    {
        if (!CommandParser.TryParse(input, out string command, out string[] args))
        {
            return;
        }

        switch (command)
        {
            case "connect":
                if (args.Length < 1)
                {
                    _history.Add(new Message("error: ip address not provided\nUsage: /connect <ip>", "System"));
                } else if (args.Length < 2)
                {
                    
                    _history.Add(new Message("error: authenticaiton token not provided\nUsage: /connect <ip> <token>", "System"));
                }
                else
                {
                    Connect(args[0], args[1]);
                }
                break;

            case "disconnect":
                HandleDisconnect();
                break;

            case "clear":
                _history.Clear();
                break;

            case "help" or "h":
                ShowHelp();
                break;

            case "exit":
                HandleDisconnect();
                Application.Current.Shutdown();
                break;

            case "ping":
                _history.Add(new Message("Pong", "System"));
                break;

            case "users":
                _history.Add(new Message("users: Unhandled", "System"));
                break;

            default:
                _history.Add(new Message($"error: unknown command '{command}'\nType /help for info.", "System"));
                break;
        }
    }

    private void HandleDisconnect()
    {
        _client?.Close();
        _isConnected = false;
        ConnectionLight.Fill = Brushes.Red;
    }

    private void ShowHelp()
    {
        string helpText = "Available commands:\n" +
                          "  /connect <ip>        Connect to a server\n" +
                          "  /disconnect          Disconnect from the server\n" +
                          "  /clear               Clear the chat history\n" +
                          "  /exit                Exits the application\n" +
                          "  /help /h             Show this help message";

        _history.Add(new Message(helpText, "System"));
    }


    private void Autocomplete()
    {
        var text = TextPrompt.Text;
        int cursor = TextPrompt.CaretIndex;

        var left = text[..cursor];
        var parts = left.Split(' ');
        var rawToken = parts.Last();

        if (string.IsNullOrWhiteSpace(rawToken))
            return;

        bool hasSlash = rawToken.StartsWith('/');
        var token = hasSlash ? rawToken[1..] : rawToken;

        var matches = _commands
            .Where(c => c.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length != 1)
            return;

        var match = hasSlash ? "/" + matches[0] : matches[0];

        TextPrompt.Text =
            text[..(cursor - rawToken.Length)] +
            match +
            text[cursor..];

        TextPrompt.CaretIndex =
            cursor - rawToken.Length + match.Length;
    }

}