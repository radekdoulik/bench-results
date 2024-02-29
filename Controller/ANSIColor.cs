namespace Controller;

public class ANSIColor
{
    public static string Color(Color code) => $"\x1B[38;5;{(int)code}m";

    public static string Reset => "\x1B[0m";
}
