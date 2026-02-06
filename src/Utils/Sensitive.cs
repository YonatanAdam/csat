namespace Utils;

public class Sensitive<T>
{
    public T Inner;
    public Sensitive(T inner) => Inner = inner;
    public override string ToString() => Global.SAFE_MODE ? "[REDACTED]" : $"{this.Inner}";
}

public static class SensitiveExtensions
{
    public static Sensitive<T> AsSensitive<T>(this T value) => new Sensitive<T>(value);
}