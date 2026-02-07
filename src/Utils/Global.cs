namespace Utils;

public class Global
{
    public static readonly bool SAFE_MODE = false;
    public static readonly int PORT = 4293;
    public static readonly string ADDRESS = "127.0.0.1";
    public static TimeSpan BAN_LIMIT = TimeSpan.FromMinutes(10);
    public static TimeSpan MESSAGE_RATE = TimeSpan.FromSeconds(1);
    public static readonly int STRIKE_LIMIT = 10;
    public static readonly int MAX_USERNAME_LENGTH = 32;
}