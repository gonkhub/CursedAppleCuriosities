using System.Collections.Generic;
using System.Text;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// Who in the run can load each Deadlock district. A district is only offered when every player has a Deadlock install
    /// that converted it to the same geometry (the same fingerprint as the host's). Each machine reports all its districts
    /// for its own players when they join and whenever a conversion state changes; the host keeps the table.
    /// </summary>
    public static class PartyCheck
    {
        private static readonly Dictionary<NetworkUser, Dictionary<string, (string Fingerprint, string Reason)>> Reports =
            new Dictionary<NetworkUser, Dictionary<string, (string, string)>>();
        private static string _lastSent = "";

        public static void Init()
        {
            NetworkingAPI.RegisterMessageType<MapStatusMessage>();
            NetworkUser.onPostNetworkUserStart += user => { if (user.isLocalPlayer) Report(user); };
            NetworkUser.onNetworkUserLost += user => Reports.Remove(user);
            RoR2Application.onUpdate += Poll;
        }

        /// <summary>This machine's status for a district: its fingerprint (empty when it can't load it) and why not.</summary>
        public static (string Fingerprint, string Reason) Local(District d)
        {
            MapConverter? c = d.Converter;
            if (c == null) return ("", "has Deadlock maps switched off");
            return c.State == MapState.Ready && c.Fingerprint != null ? (c.Fingerprint, "ready") : ("", c.State switch
            {
                MapState.NoDeadlock => "has no usable Deadlock install (" + c.Status + ")",
                MapState.Converting => "is still converting " + d.Def.Name,
                MapState.Failed => "couldn't convert " + d.Def.Name,
                _ => "hasn't converted " + d.Def.Name + " yet",
            });
        }

        /// <summary>Every district's status, one line each: id, fingerprint, reason (tab separated).</summary>
        private static string LocalAll()
        {
            var sb = new StringBuilder();
            foreach (District d in DeadlockMaps.Districts)
            {
                var (fp, reason) = Local(d);
                sb.Append(d.Id).Append('\t').Append(fp).Append('\t').Append(reason).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Resend when this machine's status changes (a conversion finishing mid-lobby).</summary>
        private static void Poll()
        {
            if (!NetworkClient.active && !NetworkServer.active) return;
            if (LocalAll() == _lastSent) return;
            foreach (NetworkUser u in NetworkUser.readOnlyLocalPlayersList) Report(u);
        }

        private static void Report(NetworkUser user)
        {
            _lastSent = LocalAll();
            new MapStatusMessage { User = user.gameObject, Status = _lastSent }.Send(NetworkDestination.Server);
        }

        internal static void ServerRecord(NetworkUser user, string status)
        {
            var table = new Dictionary<string, (string, string)>();
            foreach (string line in status.Split('\n'))
            {
                string[] f = line.Split('\t');
                if (f.Length >= 3) table[f[0]] = (f[1], f[2]);
            }
            Reports[user] = table;
            foreach (var kv in table)
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[DeadlockMap] {user.userName}: {kv.Key} {(kv.Value.Item1.Length > 0 ? "ready " + kv.Value.Item1 : kv.Value.Item2)}");
        }

        /// <summary>Server: true when every player in the run reported the host's own fingerprint for the district. Why = who isn't.</summary>
        public static bool AllReady(District d, out string why)
        {
            var (host, hostReason) = Local(d);
            if (host.Length == 0) { why = "the host " + hostReason; return false; }
            foreach (NetworkUser u in NetworkUser.readOnlyInstancesList)
            {
                if (!Reports.TryGetValue(u, out var table)) { why = $"{u.userName} hasn't reported a Deadlock install"; return false; }
                if (!table.TryGetValue(d.Id, out var r)) { why = $"{u.userName} doesn't have {d.Def.Name} (an older version of the mod?)"; return false; }
                if (r.Fingerprint.Length == 0) { why = $"{u.userName} {r.Reason}"; return false; }
                if (r.Fingerprint != host) { why = $"{u.userName} has a different Deadlock map version"; return false; }
            }
            why = "";
            return NetworkUser.readOnlyInstancesList.Count > 0;
        }
    }

    public class MapStatusMessage : INetMessage
    {
        public GameObject? User;
        /// <summary>One line per district: id, fingerprint (empty = can't load it), reason; tab separated.</summary>
        public string Status = "";

        public void Serialize(NetworkWriter w) { w.Write(User); w.Write(Status); }

        public void Deserialize(NetworkReader r) { User = r.ReadGameObject(); Status = r.ReadString(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || User == null || !User.TryGetComponent(out NetworkUser user)) return;
            PartyCheck.ServerRecord(user, Status);
        }
    }
}
