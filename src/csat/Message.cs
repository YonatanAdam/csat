namespace csat;

public class Message
{
    public Guid Id { get; set; }
    public string Text { get; set; }
    public DateTime Timestamp { get; set; }

    public Message(string message)
    {
        Id = Guid.NewGuid();
        Text = message;
        Timestamp = DateTime.Now;
    }
}