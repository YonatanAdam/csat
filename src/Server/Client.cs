using Utils;

namespace csat;

using System.Net.Sockets;

public class Client
{
    public readonly TcpClient conn;
    public DateTime last_message;
    public int strike_count;
    public bool authenticated;
    public string? Username;

    public Client(TcpClient other, DateTime last_message, int strike_count, bool authed, string username = "Unknown")
    {
        this.conn = other;
        this.last_message = last_message;
        this.strike_count = strike_count;
        this.authenticated = authed;
        this.Username = username;
    }
    
    public bool CanSendMessage(DateTime now) => (now - this.last_message) >= Global.MESSAGE_RATE;
    public bool ShouldBeBanned() => strike_count >= Global.STRIKE_LIMIT;

    public void Strike() =>  strike_count += 1;
}
