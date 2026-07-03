namespace SpeedTestThroughput;

internal sealed class AppSettings
{
    public string Host { get; set; } = "localhost";

    public uint Port { get; set; } = 5252;

    public string UserID { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool Integrated { get; set; }
}
