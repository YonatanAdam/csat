namespace _4at;

using System.Net.Sockets;

public class Client
{
    public TcpClient conn;

    public Client(TcpClient other)
    {
        this.conn = other;
    }
}
