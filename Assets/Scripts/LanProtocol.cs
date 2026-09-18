using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

// One game found on the network (or announced by us).
public class ServerInfo
{
    public string Id;
    public string Name;
    public string Address;
    public int Port;
    public int Players;
    public int MaxPlayers;
    public float LastSeen;
}

public struct LocalAddress
{
    public IPAddress Address;
    public IPAddress Broadcast;
    public string InterfaceName;
}

// Wire format + helpers for LAN game discovery (works on a real LAN and on Radmin/Hamachi-style virtual LANs).
public static class LanProtocol
{
    public const int DiscoveryPort = 47777;
    public const int GamePort = 7777;
    const string Magic = "IMPRV1";

    public static string Encode(string name, int gamePort, int players, int maxPlayers, string id)
    {
        name = (name ?? "Game").Replace("|", " ");
        return $"{Magic}|{name}|{gamePort}|{players}|{maxPlayers}|{id}";
    }

    public static bool TryDecode(string message, out ServerInfo info)
    {
        info = null;
        var parts = message.Split('|');
        if (parts.Length != 6 || parts[0] != Magic) return false;
        if (!int.TryParse(parts[2], out int port) || !int.TryParse(parts[3], out int players) ||
            !int.TryParse(parts[4], out int max))
            return false;

        info = new ServerInfo { Name = parts[1], Port = port, Players = players, MaxPlayers = max, Id = parts[5] };
        return true;
    }

    // Every usable IPv4 address of this PC together with its subnet broadcast address.
    // Announcing on each interface separately is what makes discovery work over a VPN adapter (Radmin).
    public static List<LocalAddress> GetLocalAddresses()
    {
        var result = new List<LocalAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (unicast.IPv4Mask == null) continue;

                    byte[] ip = unicast.Address.GetAddressBytes();
                    byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);

                    // Skip link-local (169.254.x.x) addresses.
                    if (ip[0] == 169 && ip[1] == 254) continue;

                    result.Add(new LocalAddress
                    {
                        Address = unicast.Address,
                        Broadcast = new IPAddress(broadcast),
                        InterfaceName = nic.Name
                    });
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Could not enumerate network interfaces: " + e.Message);
        }
        return result;
    }
}
