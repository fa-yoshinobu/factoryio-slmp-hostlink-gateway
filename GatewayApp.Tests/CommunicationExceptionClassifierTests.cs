using GatewayApp.Services;
using System.IO;
using System.Net.Sockets;

namespace GatewayApp.Tests;

public sealed class CommunicationExceptionClassifierTests
{
    [Fact]
    public void IsExpectedLocalStop_ReturnsTrueForNestedExpectedExceptions()
    {
        var exception = new IOException(
            "wrapper",
            new AggregateException(
                new OperationCanceledException(),
                new ObjectDisposedException("socket"),
                new SocketException((int)SocketError.OperationAborted)));

        Assert.True(CommunicationExceptionClassifier.IsExpectedLocalStop(exception));
    }

    [Fact]
    public void IsExpectedLocalStop_ReturnsFalseWhenAggregateContainsRealFailure()
    {
        var exception = new AggregateException(
            new OperationCanceledException(),
            new InvalidOperationException("real failure"));

        Assert.False(CommunicationExceptionClassifier.IsExpectedLocalStop(exception));
    }
}
