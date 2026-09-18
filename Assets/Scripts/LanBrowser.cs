using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Client side: listens for host announcements and keeps a list of games seen in the last few seconds.
public class LanBrowser : MonoBehaviour
{
    const float ServerTimeout = 5f;

    readonly Dictionary<string, ServerInfo> servers = new Dictionary<string, ServerInfo>();
    UdpClient listener;

    // Bumped whenever the list of visible servers changes, so the UI knows when to redraw.
    public int Version { get; private set; }
    public string Error { get; private set; }

    void OnEnable()
    {
        Error = null;
        try
        {
            listener = new UdpClient(AddressFamily.InterNetwork);
            // Several game instances on one PC (e.g. editor + build) may all listen.
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, LanProtocol.DiscoveryPort));
            listener.EnableBroadcast = true;
        }
        catch (Exception e)
        {
            Error = e.Message;
            Debug.LogWarning("LAN discovery could not start: " + e.Message);
            CloseListener();
        }
    }

    void OnDisable()
    {
        CloseListener();
        servers.Clear();
        Version++;
    }

    void Update()
    {
        float now = Time.realtimeSinceStartup;

        try
        {
            while (listener != null && listener.Available > 0)
            {
                var sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = listener.Receive(ref sender);
                if (!LanProtocol.TryDecode(Encoding.UTF8.GetString(data), out ServerInfo info)) continue;

                info.Address = sender.Address.ToString();
                info.LastSeen = now;
                string key = info.Id + "@" + info.Address;

                if (!servers.TryGetValue(key, out var existing) ||
                    existing.Players != info.Players || existing.Name != info.Name || existing.Port != info.Port)
                    Version++;
                servers[key] = info;
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        List<string> expired = null;
        foreach (var pair in servers)
        {
            if (now - pair.Value.LastSeen > ServerTimeout)
                (expired ??= new List<string>()).Add(pair.Key);
        }
        if (expired != null)
        {
            foreach (var key in expired) servers.Remove(key);
            Version++;
        }
    }

    public List<ServerInfo> GetServers()
    {
        var list = new List<ServerInfo>(servers.Values);
        list.Sort((a, b) =>
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.Address, b.Address, StringComparison.Ordinal);
        });
        return list;
    }

    void CloseListener()
    {
        if (listener == null) return;
        try { listener.Close(); } catch { }
        listener = null;
    }
}
