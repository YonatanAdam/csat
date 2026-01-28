namespace csat;

public class Message
{
    public Guid Id { get; set; }
    public string Text { get; set; }
    public string Username { get; set; }
    public DateTime Timestamp { get; set; }

    public Message(string text, string username = "", DateTime? timestamp = null)
    {
        Text = text;
        Username = username;
        Timestamp = timestamp ?? DateTime.Now;
    }
}