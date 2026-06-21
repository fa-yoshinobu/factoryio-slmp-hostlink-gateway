using GatewayApp.Models;
using System.IO;

namespace GatewayApp.Services;

public sealed class LogFileService
{
    private readonly string _directory;

    public LogFileService()
        : this(AppContext.BaseDirectory)
    {
    }

    public LogFileService(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public void WriteGatewayLog(LogEntry entry)
    {
        AppendText("gateway.log", $"{DateTime.Now:yyyy-MM-dd} {entry.FullText}{Environment.NewLine}");
    }

    public void ClearGatewayLog()
    {
        ClearLog("gateway.log");
    }

    public void ClearAllLogs()
    {
        ClearLog("gateway.log");
        ClearLog("error.log");
    }

    public void WriteExceptionLog(Exception exception)
    {
        AppendText("error.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {exception}\r\n");
    }

    private void AppendText(string fileName, string text)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        File.AppendAllText(path, text);
    }

    private void ClearLog(string fileName)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, fileName), string.Empty);
    }
}
