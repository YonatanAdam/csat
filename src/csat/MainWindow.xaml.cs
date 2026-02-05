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
    private readonly ObservableCollection<Message> history = new ObservableCollection<Message>();
    private readonly string[] commands = ["connect", "disconnect", "help", "clear", "exit", "ping", "users"];
    private List<string> clientMessageHistory = new List<string>();
    private int history_index = -1;
    private TcpClient? client = null;
    private NetworkStream? activeStream;
    private bool isConnected;
    private readonly int Port = 4293;

    public MainWindow()
    {
        InitializeComponent();
        WelcomeMessage();

        TextPrompt.Focus();
        ChatHistory.ItemsSource = history;

        history.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Ensure scroll to the very last item added
                ChatHistory.ScrollIntoView(history[^1]);
            }
        };
    }

    private void WelcomeMessage()
    {
        string msg = "Welcome!\n" +
                     "Try:  /connect <ip>        Connect to a server\n" +
                     "or   /help /h               to see all commands";
        history.Add(new Message(msg, "Server"));
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SendAsync();
        }
        catch (Exception ex)
        {
            history.Add(new Message($"Unexpected error: {ex.Message}", "System"));
        }
    }

    private async Task SendAsync()
    {
        var messageText = TextPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        history.Add(new Message(messageText, "You"));
        clientMessageHistory.Add(messageText);
        history_index = -1;
        TextPrompt.Clear();

        if (messageText.StartsWith('/'))
        {
            HandleCommand(messageText);
            return;
        }

        if (activeStream == null || !isConnected)
            return;

        var bytes = Encoding.UTF8.GetBytes(messageText + "\n");
        await activeStream.WriteAsync(bytes);

    }

    private void Connect(string ip)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Dispatcher.BeginInvoke(() => history.Add(new Message("Connecting to server...", "Server")));

                client = new TcpClient();
                await client.ConnectAsync(ip, Port);
                activeStream = client.GetStream();
                isConnected = true;

                await Dispatcher.BeginInvoke(() =>
                {
                    history.Add(new Message("Connected to server", "Server"));
                    ConnectionLight.Fill = Brushes.Orange;
                });

                var buffer = new byte[1024];
                while (client.Connected && isConnected)
                {
                    int n;
                    try
                    {
                        n = await activeStream.ReadAsync(buffer);
                        if (n == 0) break; // Server disconnected
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    var receivedText = Encoding.UTF8.GetString(buffer, 0, n).TrimEnd();

                    await Dispatcher.BeginInvoke(() => // parse message
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

                        history.Add(new Message(content, username));

                        if (receivedText.Contains("Welcome"))
                        {
                            ConnectionLight.Fill = Brushes.LimeGreen;
                        }
                    });
                }

            }
            catch (Exception e)
            {
                await Dispatcher.BeginInvoke(() => history.Add(new Message($"Failed connecting to server: {e.Message}", "Server")));
            }
            finally
            {
                isConnected = false;
                activeStream?.Dispose();
                client?.Dispose();
                await Dispatcher.BeginInvoke(() => history.Add(new Message("Disconnected from server", "Server")));
                await Dispatcher.BeginInvoke(() => ConnectionLight.Fill = Brushes.Red);
            }
        });
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e) => client?.Close();

    private void TextPrompt_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TextPrompt.Text.StartsWith('/') && e.Key == Key.Tab)
        {
            Autocomplete();
            e.Handled = true;
        }

        if (clientMessageHistory.Count == 0) return;

        if ((e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.Up)
        {
            e.Handled = true;
            if (history_index < clientMessageHistory.Count - 1)
            {
                history_index++;
            }
            else
            {
                // Wrap to the beginning (empty prompt)
                history_index = -1;
            }

            if (history_index == -1)
            {
                TextPrompt.Text = string.Empty;
            }
            else
            {
                TextPrompt.Text = clientMessageHistory[^(history_index + 1)];
                TextPrompt.CaretIndex = TextPrompt.Text.Length;
            }
        }
        // Ctrl+N for next (down in history)
        else if ((e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) || e.Key == Key.Down)
        {
            e.Handled = true;
            if (history_index > -1)
            {
                history_index--;
            }
            else
            {
                // Wrap to the end (most recent message)
                history_index = clientMessageHistory.Count - 1;
            }

            if (history_index == -1)
            {
                TextPrompt.Text = string.Empty;
            }
            else
            {
                TextPrompt.Text = clientMessageHistory[^(history_index + 1)];
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
                    history.Add(new Message("error: ip address not provided\nUsage: /connect <ip>", "Server"));
                }
                else
                {
                    Connect(args[0]);
                }
                break;

            case "disconnect":
                HandleDisconnect();
                break;

            case "clear":
                history.Clear();
                break;

            case "help" or "h":
                ShowHelp();
                break;

            case "exit":
                HandleDisconnect();
                Application.Current.Shutdown();
                break;

            case "ping":
                history.Add(new Message("Pong", "System"));
                break;

            case "users":
                history.Add(new Message("users: Unhandled", "System"));
                break;

            default:
                history.Add(new Message($"error: unknown command '{command}'\nType /help for info.", "Server"));
                break;
        }
    }

    private void HandleDisconnect()
    {
        client?.Close();
        isConnected = false;
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

        history.Add(new Message(helpText, "Server"));
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

        var matches = commands
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