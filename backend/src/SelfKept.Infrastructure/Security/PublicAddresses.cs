using System.Net;
using System.Net.Sockets;

namespace SelfKept.Infrastructure.Security;

/// <summary>
/// Which addresses the server may fetch on someone else's say-so (an OAuth client's metadata
/// document). Used before the fetch and again by the "oauth-cimd" HttpClient on the address it
/// actually dials, so a DNS answer that changes in between cannot reach the home network.
/// </summary>
public static class PublicAddresses
{
  /// <summary>
  /// Publicly routable by exclusion: not loopback, unspecified, private (RFC 1918), link-local,
  /// CGNAT (100.64/10, which is also the tailnet range), unique-local IPv6, multicast or reserved.
  /// </summary>
  public static bool IsPublic(IPAddress ip)
  {
    if (ip.IsIPv4MappedToIPv6)
      ip = ip.MapToIPv4();
    if (IPAddress.IsLoopback(ip))
      return false;
    if (ip.AddressFamily == AddressFamily.InterNetworkV6)
    {
      if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
        return false;
      return !(ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal);
    }
    var b = ip.GetAddressBytes();
    if (b.Length != 4) return false;
    if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
    if (b[0] == 169 && b[1] == 254) return false;
    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
    if (b[0] == 192 && b[1] == 168) return false;
    if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
    if (b[0] >= 224) return false;
    return true;
  }
}
