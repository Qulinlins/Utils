using System.Net.Sockets;

namespace Qulinlin.Network.Http;

public class SocketSession(Socket socket)
{
    private Socket _socket = socket;

    public Stream GetNetworkStream()
    {
        return new NetworkStream(_socket,false);
    }
}