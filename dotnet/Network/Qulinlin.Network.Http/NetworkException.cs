namespace Qulinlin.Network.Http;

public class NetworkException : Exception
{
    public NetworkException(){}

    public NetworkException(string message):base(message){}

    public NetworkException(string message,Exception? inner) : base(message, inner)
    {
        
    }

    public NetworkException(NetworkErrorCode code):this(GetErrorDescription(code)){}

    public static string GetErrorDescription(NetworkErrorCode code)
    {
        return code switch{
            NetworkErrorCode.ProtocolError => "Network protocol error.",
            NetworkErrorCode.Aborted => "The connection was aborted.",
            NetworkErrorCode.Reset => "The connection was reset",
            NetworkErrorCode.Timedout => "Connection timed out",
            NetworkErrorCode.Refused => "The target server actively refuses.",
            NetworkErrorCode.Unreachable => "The specified network address is unreachable.",
            NetworkErrorCode.HostNotFound => "The specified network address does not exist.",
            _ => "Unknown Error"
        };
    }
}

public enum NetworkErrorCode
{
    Refused = 10061,
    Timedout = 10060,
    Aborted = 10053,
    Reset = 10054,
    Unreachable = 10065,
    HostNotFound = 11061,
    ProtocolError = 10041
}