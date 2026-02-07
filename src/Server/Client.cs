using Utils;

namespace csat;

using System.Net.Sockets;

public class Client
{
    public readonly TcpClient Conn;
    public DateTime LastMessage;
    public int StrikeCount;
    public bool Authenticated;
    public string? Username;

    public Client(TcpClient other, DateTime lastMessage, int strikeCount, bool authed, string username = "Unknown")
    {
        this.Conn = other;
        this.LastMessage = lastMessage;
        this.StrikeCount = strikeCount;
        this.Authenticated = authed;
        this.Username = username;
    }
    
    public bool CanSendMessage(DateTime now) => (now - this.LastMessage) >= Global.MESSAGE_RATE;
    public bool ShouldBeBanned() => StrikeCount >= Global.STRIKE_LIMIT;

    public void Strike() =>  StrikeCount += 1;
}
