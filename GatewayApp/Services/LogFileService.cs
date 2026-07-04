using GatewayApp.Models;
using System.IO;
using System.Text;

namespace GatewayApp.Services;

public sealed class LogFileService
{
    public const long DefaultMaxLogBytes = 10 * 1024 * 1024;

    private readonly string _directory;
    private readonly long _maxLogBytes;

    public LogFileService()
        : this(AppContext.BaseDirectory)
    {
    }

    public LogFileService(string directory)
        : this(directory, DefaultMaxLogBytes)
    {
    }

    public LogFileService(string directory, long maxLogBytes)
    {
        if (maxLogBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLogBytes), maxLogBytes, "Log size limit must be positive.");
        }

        _directory = Path.GetFullPath(directory);
        _maxLogBytes = maxLogBytes;
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
        RotateIfNeeded(path, Encoding.UTF8.GetByteCount(text));
        File.AppendAllText(path, text);
    }

    private void ClearLog(string fileName)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, string.Empty);
        var rotatedPath = GetRotatedPath(path);
        if (File.Exists(rotatedPath))
        {
            File.Delete(rotatedPath);
        }
    }

    private void RotateIfNeeded(string path, int appendByteCount)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var currentBytes = new FileInfo(path).Length;
        if (currentBytes + appendByteCount <= _maxLogBytes)
        {
            return;
        }

        var rotatedPath = GetRotatedPath(path);
        if (File.Exists(rotatedPath))
        {
            File.Delete(rotatedPath);
        }

        File.Move(path, rotatedPath);
    }

    private static string GetRotatedPath(string path) => $"{path}.1";
}
