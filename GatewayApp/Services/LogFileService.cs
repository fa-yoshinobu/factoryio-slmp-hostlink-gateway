using GatewayApp.Models;
using System.IO;

namespace GatewayApp.Services;

public sealed class LogFileService
{
    private const long MaxLogBytes = 1_000_000;
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
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "gateway.log"), string.Empty);
    }

    public void WriteExceptionLog(Exception exception)
    {
        AppendText("error.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {exception}\r\n");
    }

    private void AppendText(string fileName, string text)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        RotateLogIfNeeded(path, fileName);
        File.AppendAllText(path, text);
    }

    private static void RotateLogIfNeeded(string path, string fileName)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length <= MaxLogBytes)
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {fileName} rotated because it exceeded {MaxLogBytes} bytes.");
    }
}
