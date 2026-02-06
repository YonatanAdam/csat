using System.Net.Sockets;

namespace csat;

public abstract record Message
{
    public record ClientConnected(TcpClient client) : Message;
    public record ClientDisconnected(string author_addr) : Message;
    public record NewMessage(string author_addr, Byte[] Data) : Message;
    public record AdminCommand(string command, string[] args) : Message;
}