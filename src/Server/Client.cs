namespace csat;

using System.Net.Sockets;

public class Client
{
    public TcpClient conn;
    public DateTime last_message;
    public int strike_count;
    public bool authenticated;
    public string? Username;

    public Client(TcpClient other, DateTime last_messsage, int strike_count, bool authed, string username = "Unknown")
    {
        this.conn = other;
        this.last_message = last_messsage;
        this.strike_count = strike_count;
        this.authenticated = authed;
        this.Username = username;
    }
}
