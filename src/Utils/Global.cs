namespace Utils;

public class Global
{
    public static bool SAFE_MODE = false;
    public static int PORT = 4293;
    public static string ADDRESS = "127.0.0.1";
    public static TimeSpan BAN_LIMIT = TimeSpan.FromMinutes(10);
    public static TimeSpan MESSAGE_RATE = TimeSpan.FromSeconds(1);
    public static int STRIKE_LIMIT = 10;
    public static int MAX_USERNAME_LENGTH = 32;
}