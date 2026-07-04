using GatewayApp.Models;
using GatewayApp.Services;
using System.IO;

namespace GatewayApp.Tests;

public sealed class LogFileServiceTests
{
    [Fact]
    public void WriteGatewayLog_RotatesCurrentLogWhenLimitWouldBeExceeded()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var service = new LogFileService(tempDir, maxLogBytes: 64);
            var gatewayLogPath = Path.Combine(tempDir, "gateway.log");
            var rotatedPath = $"{gatewayLogPath}.1";
            File.WriteAllText(gatewayLogPath, new string('x', 60));

            service.WriteGatewayLog(new LogEntry("after-limit"));

            Assert.True(File.Exists(rotatedPath));
            Assert.Contains("after-limit", File.ReadAllText(gatewayLogPath), StringComparison.Ordinal);
            Assert.DoesNotContain("after-limit", File.ReadAllText(rotatedPath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteExceptionLog_RotatesErrorLogWhenLimitWouldBeExceeded()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var service = new LogFileService(tempDir, maxLogBytes: 64);
            var errorLogPath = Path.Combine(tempDir, "error.log");
            var rotatedPath = $"{errorLogPath}.1";
            File.WriteAllText(errorLogPath, new string('x', 60));

            service.WriteExceptionLog(new InvalidOperationException("after-limit"));

            Assert.True(File.Exists(rotatedPath));
            Assert.Contains("after-limit", File.ReadAllText(errorLogPath), StringComparison.Ordinal);
            Assert.DoesNotContain("after-limit", File.ReadAllText(rotatedPath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public void ClearAllLogs_ClearsCurrentLogsAndDeletesRotatedLogs()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var service = new LogFileService(tempDir, maxLogBytes: 64);
            var gatewayLogPath = Path.Combine(tempDir, "gateway.log");
            var errorLogPath = Path.Combine(tempDir, "error.log");
            File.WriteAllText(gatewayLogPath, "old gateway");
            File.WriteAllText(errorLogPath, "old error");
            File.WriteAllText($"{gatewayLogPath}.1", "old gateway archive");
            File.WriteAllText($"{errorLogPath}.1", "old error archive");

            service.ClearAllLogs();

            Assert.Equal(string.Empty, File.ReadAllText(gatewayLogPath));
            Assert.Equal(string.Empty, File.ReadAllText(errorLogPath));
            Assert.False(File.Exists($"{gatewayLogPath}.1"));
            Assert.False(File.Exists($"{errorLogPath}.1"));
        }
        finally
        {
            DeleteTempDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gateway-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
