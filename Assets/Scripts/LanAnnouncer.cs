using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Host side: shouts "I'm a game!" over UDP broadcast once a second on every network adapter.
public class LanAnnouncer : MonoBehaviour
{
    public string ServerName = "Game";
    public int GamePort = LanProtocol.GamePort;
    public int MaxPlayers = 8;
    public Func<int> PlayerCount = () => 1;

    readonly List<UdpClient> senders = new List<UdpClient>();
    readonly List<IPEndPoint> targets = new List<IPEndPoint>();
    readonly string id = Guid.NewGuid().ToString("N").Substring(0, 8);
    float nextSend;
    float nextRefresh;

    void OnEnable()
    {
        RebuildSockets();
        nextSend = 0f;
    }

    void OnDisable() => CloseSockets();

    void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now >= nextRefresh)
        {
            RebuildSockets(); // adapters can appear/disappear (e.g. Radmin connecting)
            nextRefresh = now + 10f;
        }

        if (now < nextSend) return;
        nextSend = now + 1f;

        byte[] payload = Encoding.UTF8.GetBytes(LanProtocol.Encode(ServerName, GamePort, PlayerCount(), MaxPlayers, id));
        for (int i = 0; i < senders.Count; i++)
        {
            try
            {
                senders[i].Send(payload, payload.Length, targets[i]);
                senders[i].Send(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, LanProtocol.DiscoveryPort));
            }
            catch (SocketException) { /* adapter went away; next rebuild drops it */ }
            catch (ObjectDisposedException) { }
        }
    }

    void RebuildSockets()
    {
        CloseSockets();
        foreach (var local in LanProtocol.GetLocalAddresses())
        {
            try
            {
                // Binding to the adapter's own address makes the packet leave through that adapter.
                var client = new UdpClient(new IPEndPoint(local.Address, 0)) { EnableBroadcast = true };
                senders.Add(client);
                targets.Add(new IPEndPoint(local.Broadcast, LanProtocol.DiscoveryPort));
            }
            catch (SocketException) { }
        }
    }

    void CloseSockets()
    {
        foreach (var s in senders)
        {
            try { s.Close(); } catch { }
        }
        senders.Clear();
        targets.Clear();
    }
}
