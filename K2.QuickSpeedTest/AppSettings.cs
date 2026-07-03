namespace SpeedTestThroughput;

internal sealed class AppSettings
{
    public string Host { get; init; } = "localhost";

    public uint Port { get; init; } = 5252;

    public string UserID { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool Integrated { get; init; }
}
