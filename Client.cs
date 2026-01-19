namespace _4at;

using System.Net.Sockets;

public class Client
{
    public TcpClient conn;
    public DateTime last_messsage;
    public int strike_count;

    public Client(TcpClient other, DateTime last_messsage, int strike_count)
    {
        this.conn = other;
        this.last_messsage = last_messsage;
        this.strike_count = strike_count;
    }
}
