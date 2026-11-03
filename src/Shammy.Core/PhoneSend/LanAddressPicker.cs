using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Shammy.Core.PhoneSend;

/// <summary>What we need to know about one network adapter to judge whether a phone can reach it.</summary>
public sealed record NetworkCandidate(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    OperationalStatus Status,
    bool HasGateway,
    IReadOnlyList<IPAddress> Addresses);

/// <summary>
/// Picks the IPv4 address a phone on the same Wi-Fi can reach. A typical laptop has Hyper-V, WSL,
/// VPN and Bluetooth adapters too, and a QR code pointing at any of those simply never loads.
/// </summary>
public static class LanAddressPicker
{
    static readonly string[] VirtualHints =
    {
        "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "vbox", "docker", "wsl", "loopback",
        "vpn", "tap-", "tap ", "tun", "wireguard", "tailscale", "zerotier", "hamachi", "openvpn", "nordlynx",
        "anyconnect", "fortinet", "globalprotect", "pangp", "npcap", "bluetooth", "teredo", "isatap", "wan miniport",
    };

    /// <summary>Higher is better; <see cref="int.MinValue"/> means unusable.</summary>
    public static int Score(NetworkCandidate nic, IPAddress address)
    {
        if (nic.Status != OperationalStatus.Up) return int.MinValue;
        if (nic.Type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) return int.MinValue;
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return int.MinValue;
        var b = address.GetAddressBytes();
        if (b[0] == 0 || (b[0] == 169 && b[1] == 254) || b[0] >= 224) return int.MinValue;

        int score = 0;
        if (nic.HasGateway) score += 100;
        score += nic.Type switch
        {
            NetworkInterfaceType.Wireless80211 => 40,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx => 30,
            NetworkInterfaceType.Ppp => -40,
            _ => -10,
        };
        var text = (nic.Name + " " + nic.Description).ToLowerInvariant();
        if (VirtualHints.Any(text.Contains)) score -= 150;

        bool isPrivate = b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
        bool isCgnat = b[0] == 100 && b[1] is >= 64 and <= 127; // Tailscale and carrier NAT
        if (isPrivate) score += 30;
        if (isCgnat) score -= 60;
        // 192.168.x is the classic home router range; Hyper-V's default switch tends to use 172.x.
        if (b[0] == 192 && b[1] == 168) score += 5;
        return score;
    }

    public static IPAddress? Pick(IEnumerable<NetworkCandidate> nics)
    {
        IPAddress? best = null;
        int bestScore = int.MinValue;
        foreach (var nic in nics)
        {
            foreach (var a in nic.Addresses)
            {
                int s = Score(nic, a);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = a;
                }
            }
        }
        return best;
    }

    /// <summary>The machine's adapters as candidates. Never throws; an empty list means no network.</summary>
    public static IReadOnlyList<NetworkCandidate> FromSystem()
    {
        var list = new List<NetworkCandidate>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    var props = nic.GetIPProperties();
                    bool gateway = props.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    var addresses = props.UnicastAddresses
                        .Select(u => u.Address)
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                        .ToList();
                    list.Add(new NetworkCandidate(nic.Name, nic.Description, nic.NetworkInterfaceType,
                        nic.OperationalStatus, gateway, addresses));
                }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException) { }
        return list;
    }
}
