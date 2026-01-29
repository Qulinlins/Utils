using System.Net;
using System.Net.Sockets;

namespace Qulinlin.Network.Http.Abstractions;

public abstract class DnsResolver
{
    public virtual async Task<IEnumerable<InternetAddress>> GetAddressAsync(string hostName)
    {
        var result = await Dns.GetHostEntryAsync(hostName);
        var data = new List<InternetAddress>();
        foreach(var address in result.AddressList)
        {
            data.Add(new InternetAddress(
                address,
                address.AddressFamily == AddressFamily.InterNetwork?AddressType.Inet4:AddressType.Inet6)
            );
        }
        return data;
    }
}