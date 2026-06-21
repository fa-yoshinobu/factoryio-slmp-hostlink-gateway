using System.Globalization;

namespace GatewayApp.Models;

public sealed class LogEntry
{
    public LogEntry(string message)
    {
        Time = DateTime.Now;
        Message = message;
    }

    public DateTime Time { get; }

    public string Message { get; }

    public string TimeText => Time.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);

    public string FullText => $"{TimeText}  {Message}";
}
