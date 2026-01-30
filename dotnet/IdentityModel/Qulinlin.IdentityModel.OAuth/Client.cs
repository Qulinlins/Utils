using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Qulinlin.IdentityModel.OAuth;

public class SimpleOAuthClient
{

    public int MaxRetry {get;set;}

    public TimeSpan MaxTimeout {get;set;}

    private string _clientId;

    // RFC 6749 required endpoint
    private string _authorizeEndpoint;
    private string _tokenEndpoint;

    private string _redirectUri;

    private string? _state;
    private string? _pkce;

    private static readonly RandomNumberGenerator _generator = RandomNumberGenerator.Create();
    private static readonly Random _random = new();

    private string? _clientSecret;

    // Extend endpoint define in RFC 8628
    private string? _deviceFlowEndpoint;

    public SimpleOAuthClient(
        string clientId,
        string tokenEp,
        string authorizeEp,
        string redirect,
        string? clientSecret
    )
    {
        if(string.IsNullOrWhiteSpace(clientId)) 
            throw new ArgumentException(nameof(clientId));
        if(string.IsNullOrWhiteSpace(redirect)) 
            throw new ArgumentException(nameof(redirect));
        if(string.IsNullOrWhiteSpace(authorizeEp)) 
            throw new ArgumentException(nameof(authorizeEp));

        _clientId = clientId;
        _tokenEndpoint = tokenEp;
        _authorizeEndpoint = authorizeEp;
        _redirectUri = redirect;
        _clientSecret = clientSecret;
    }

    public SimpleOAuthClient(
        string clientId,
        string tokenEp,
        string authorizeEp,
        string redirect,
        string clientSecret,
        string deviceEp
        ): this(clientId, tokenEp, authorizeEp,redirect,clientSecret)
    {
        _deviceFlowEndpoint = deviceEp;
    }

    public HttpClient Client;
    public Func<HttpClient>? GetClient;

    public string GetAuthorizeUri(string[] scopes)
    {
        for(var i = 0;i < 10;i++) _state += _random.Next(0,65535);
        var pkce = new byte[96];
        _generator.GetBytes(pkce);
        using var sha256 = SHA256.Create();
        _pkce = GetB64StrUrlSafe(pkce);
        var challengeSha256 = sha256.ComputeHash(
            Encoding.UTF8.GetBytes(_pkce)
            );
        var challenge = GetB64StrUrlSafe(challengeSha256);
        var builder = new StringBuilder(_authorizeEndpoint);
        builder.Append($"?state={_state}&redirect_uri={_redirectUri}");
        builder.Append($"&scope={string.Join(" ",scopes)}&code_challenge_method=S256");
        builder.Append($"&code_challenge={challenge}&response_type=code");
        // keep client id safe
        builder.Append($"&client_id={_clientId}");
        return builder.ToString();
    }

    public async Task<AuthorizeResult?> AcquireTokenWithAuthorizeAsync(string code)
    {
        Exception? lastEx = null;
        var kvps = new List<KeyValuePair<string,string>>([
            new("grant_type", "authorization_code"),
            new("client_id", _clientId),
            new("redirect_uri", _redirectUri),
            new("code", code),
        ]);
        if(!string.IsNullOrWhiteSpace(_clientSecret)) kvps.Add(
            new("client_secret",_clientSecret!)
        );
        if(!string.IsNullOrWhiteSpace(_pkce)) kvps.Add(
            new("code_verifier", _pkce!)
        );
        for(var i = 0; i < MaxRetry; i++)
        {
            try{
                var client = GetClient?.Invoke() ?? (Client??=new HttpClient());
                using var request = new HttpRequestMessage(HttpMethod.Post,_tokenEndpoint);
                request.Headers.Accept.Add(new("application/json"));

                using var content = new FormUrlEncodedContent(kvps);
                request.Content = content;
                using var response = await client.SendAsync(request);
                return JsonSerializer.Deserialize<AuthorizeResult>(await response.Content.ReadAsStringAsync());
                // sb microsoft
            }
            catch (HttpRequestException ex)
            {
                lastEx = ex;
            }
            catch (TaskCanceledException ex)
            {
                lastEx = ex;
            }
        }
        throw new AuthenticationException("Unable to connect server.", lastEx);
    }

    public async Task<DeviceFlowData?> GetCodePairAsync(string[] scopes)
    {
        if(string.IsNullOrWhiteSpace(_deviceFlowEndpoint)) 
            throw new ArgumentException("Device flow endpoint is unset.");
        using var content = new FormUrlEncodedContent([
            new("client_id", _clientId),
            new("scope", string.Join("", scopes))
        ]);
        Exception? lastEx = null;
        for(var i = 0; i < MaxRetry; i++)
        {
            var client = GetClient?.Invoke() ?? (Client??=new HttpClient());
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,_deviceFlowEndpoint);
                request.Content = content;
                request.Headers.Accept.Add(new("application/json"));
                using var response = await client.SendAsync(request);
                return JsonSerializer.Deserialize<DeviceFlowData>(
                    await response.Content.ReadAsStringAsync());
            }catch(HttpRequestException ex)
            {
                lastEx = ex;
            }
            catch (TaskCanceledException ex)
            {
                lastEx = ex;
            }
        }
        throw new AuthenticationException("Unable to connect server",lastEx);
    }
    
    public async Task<AuthorizeResult?> AcquireTokenWithDeviceFlowAsync(DeviceFlowData data,string[]? scopes = null)
    {
        var errorCount = 0;
        Exception? lastEx = null;
        var kvps = new List<KeyValuePair<string,string>>(
            [
                new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                new("client_id", _clientId),
                new("device_code",data.DeviceCode!)
            ]
        );
        if(scopes is not null) kvps.Add(new("scope",string.Join(" ", scopes)));
        using var content = new FormUrlEncodedContent(kvps);
        while (true)
        {
            var client = GetClient?.Invoke() ?? (Client??=new HttpClient());
            await Task.Delay(TimeSpan.FromSeconds(data.Interval));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,_tokenEndpoint);
                request.Headers.Accept.Add(new("application/json"));
                request.Content = content;
                using var response = await client.SendAsync(request);
                var result = JsonSerializer.Deserialize<AuthorizeResult>(
                    await response.Content.ReadAsStringAsync()
                );
                // Some local proxy may return 502 Bad Gateway when connect timed out.
                // So include 502/504 for retry 
                if(response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    response.StatusCode == HttpStatusCode.BadGateway || 
                        response.StatusCode == HttpStatusCode.GatewayTimeout || 
                            // OAuth server may be use Cloudflare or Tencent EdgeOne/Aliyun ESA
                            // their return 522 
                            response.StatusCode == (HttpStatusCode)522) 
                        throw new HttpRequestException();
                if(result?.AccessToken is not null) return result;
                switch (result?.Error)
                {
                    case null:
                    case "slow_down":
                        data.Interval += 5;
                        continue;
                    case "authorization_declined":
                    case "access_denied":
                        throw new UnauthorizedAccessException(result.Error);
                    case "temporarily_unavailable":    
                        if(errorCount > MaxRetry) 
                            throw new AuthenticationException(result?.Error);
                        errorCount++;
                        continue;
                    default:
                        throw new AuthenticationException($"Authorize Error: {result.Error} {result.ErrorDescription}");
                }
            }catch(TaskCanceledException ex)
            {
                if(errorCount > MaxRetry) break;
                errorCount++;
                lastEx = ex;
            }
            catch(HttpRequestException ex)
            {
                if(errorCount > MaxRetry) break;
                errorCount++;
                lastEx = ex;
            }
        }
        throw new AuthenticationException("Unable connect to server",lastEx);
    }

    public static string GetB64StrUrlSafe(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace("+","-").Replace("/","_");
    }
    //public static byte[] FromB64StrUrlSafe
}