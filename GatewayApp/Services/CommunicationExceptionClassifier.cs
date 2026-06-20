using System.IO;
using System.Net.Sockets;

namespace GatewayApp.Services;

internal static class CommunicationExceptionClassifier
{
    public static bool IsExpectedLocalStop(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => true,
            ObjectDisposedException => true,
            SocketException socketException => IsOperationAborted(socketException),
            IOException { InnerException: { } innerException } => IsExpectedLocalStop(innerException),
            AggregateException aggregateException => aggregateException
                .Flatten()
                .InnerExceptions
                .All(IsExpectedLocalStop),
            _ => false,
        };
    }

    private static bool IsOperationAborted(SocketException exception)
    {
        return exception.SocketErrorCode == SocketError.OperationAborted
            || exception.NativeErrorCode == 995;
    }
}
