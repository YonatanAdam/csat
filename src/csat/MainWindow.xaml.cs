using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace csat;

public partial class MainWindow : Window
{
    public ObservableCollection<Message> history = new ObservableCollection<Message>();
    private TcpClient? client = null;
    private NetworkStream? activeStream;
    private bool isConnected;
    public bool isAuthenticated;
    private List<string> clientMessageHistory = new List<string>();
    private int history_index = -1;

    private string[] commands = ["connect", "disconnect", "help", "clear", "exit"];
    private static List<int> l1 = [1, 2, 3, 4];
    private static List<int> l2 = [5, 6, 7, 8];

    private List<int> d = [..l1, ..l2];

    public MainWindow()
    {
        InitializeComponent();

        TextPrompt.Focus();
        ChatHistory.ItemsSource = history;

        history.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Ensure we scroll to the very last item added
                ChatHistory.ScrollIntoView(history[^1]);
            }
        };
    }

    private bool isCommand(string text)
    {
        if (text.StartsWith('/'))
        {
            var comands = text[1..].Split(' ');
            if (commands.Contains(comands[0])) return true;
        }

        return false;
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextPrompt.Text)) return;

        var messageText = TextPrompt.Text.Trim();
        clientMessageHistory.Add(messageText);
        var message = new Message(messageText);

        history.Add(message);

        TextPrompt.Clear();

        if (isCommand(messageText))
        {
            HandleCommand(messageText);
        }
        else
        {
            if (activeStream != null && client?.Connected == true)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(messageText);
                    await activeStream.WriteAsync(bytes, 0, bytes.Length);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        history.Add(new Message($"[System]: Send failed - {ex.Message}"));
                    });
                }
            }
        }
    }

    private void Connect(string ip)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Dispatcher.Invoke(() => history.Add(new Message("[System]: Connecting to server...")));

                client = new TcpClient();
                await client.ConnectAsync(ip, 4293);
                activeStream = client.GetStream();

                Dispatcher.Invoke(() => history.Add(new Message("[System]: Connected to server")));
                isConnected = true;

                var buffer = new byte[1024];
                while (client.Connected && isConnected)
                {
                    try
                    {
                        int n = await activeStream.ReadAsync(buffer, 0, buffer.Length);
                        if (n == 0) break; // Server disconnected

                        var receivedText = Encoding.UTF8.GetString(buffer, 0, n);

                        Dispatcher.Invoke(() =>
                        {
                            history.Add(new Message($"{receivedText.TrimEnd()}"));

                            if (receivedText.Contains("Welcome"))
                            {
                                isAuthenticated = true;
                                history.Add(new Message("[System]: Authentication successful!"));
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        Dispatcher.Invoke(() => history.Add(new Message($"[System]: Failed reading from server: {e.Message}")));

                        break;
                    }
                }

                Dispatcher.Invoke(() => history.Add(new Message("[System]: Disconnected from server")));
            }
            catch (Exception e)
            {
                Dispatcher.Invoke(() => history.Add(new Message($"[System]: Failed connecting to server: {e.Message}")));

                Console.WriteLine(e);
                throw;
            }
            finally
            {
                activeStream?.Dispose();
                client?.Dispose();
            }
        });
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e) => client?.Close();

    private void TextPrompt_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (TextPrompt.Text.StartsWith('/') && e.Key == Key.Tab)
        {
            Autocomplete();
            e.Handled = true;
        }

        if (clientMessageHistory.Count == 0) return;

        // TODO: handle key up/down messages history
        if (e.Key == Key.Up)
        {
            e.Handled = true;

        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
        }
    }


    private void HandleCommand(string input)
    {
        if (!input.StartsWith('/'))
            return;

        var parts = input[1..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return;

        var command = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        switch (command)
        {
            case "connect":
                {
                    if (args.Length < 1)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            history.Add(new Message("error: ip not provided"));
                            history.Add(new Message("usage:  /connect <ip>        Connect to a server"));
                        });
                    }
                    else
                    {
                        var ip = args[0];
                        Connect(ip);
                    }
                }
                break;


            case "disconnect":
                {
                    HandleDisconnect();
                }
                break;


            case "clear":
                {
                    Dispatcher.Invoke(() => history.Clear());
                }
                break;

            case "help":
                {
                    Dispatcher.Invoke(() =>
                    {
                        history.Add(new Message("Available commands:"));
                        history.Add(new Message("  /connect <ip>        Connect to a server"));
                        history.Add(new Message("  /disconnect          Disconnect from the server"));
                        history.Add(new Message("  /clear               Clear the chat history"));
                        history.Add(new Message("  /exit                Exits the application"));
                        history.Add(new Message("  /help                Show this help message"));
                    });
                }
                break;

            case "exit":
                {
                    HandleDisconnect();
                    Application.Current.Shutdown();

                }
                break;

            default:
                {
                    Dispatcher.Invoke(() =>
                    {
                        history.Add(new Message($"error: unknown command '{command}'"));
                        history.Add(new Message("Type /help for more information."));
                    });
                    break;
                }

        }
    }

    private void HandleDisconnect()
    {
        client?.Close();
        isConnected = false;
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




















