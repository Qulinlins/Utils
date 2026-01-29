using System.Net;

namespace Qulinlin.Network.Http;

public class DnsResult(InternetAddress[] address)
{
    public InternetAddress[] Addresses = address;
}

public class InternetAddress(IPAddress address,AddressType type)
{
    public IPAddress Address = address;
    public AddressType Type = type;
}

public enum AddressType
{
    Inet6,
    Inet4
}