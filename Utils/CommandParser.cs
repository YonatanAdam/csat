namespace Utils;

public static class CommandParser
{
    // Return a 'Success' boolean along with the data to make it easier for the caller
    public static bool TryParse(string? input, out string command, out string[] args)
    {
        command = string.Empty;
        args = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return false;

        // Use '1..' to skip the '/'
        var parts = input[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return false;

        command = parts[0].ToLowerInvariant();
        args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
        
        return true;
    }
}