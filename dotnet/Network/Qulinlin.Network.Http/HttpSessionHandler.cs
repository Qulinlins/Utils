namespace Qulinlin.Network.Http;

public class HttpSessionHandler(NetworkAccessManager access) : DelegatingHandler
{
    private NetworkAccessManager _access = access;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {

        var response = new HttpResponseMessage();
        return Task.Run(() => response);
    }
}