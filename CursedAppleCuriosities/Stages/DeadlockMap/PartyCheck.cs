using System.Collections.Generic;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    /// <summary>
    /// Who in the run can load a Deadlock map stage. A stage built from Deadlock is only offered when every player has a
    /// Deadlock install that converted the section to the same geometry (same fingerprint as the host). Each machine
    /// reports for its own players when they join and whenever its conversion state changes; the host keeps the table.
    /// </summary>
    public static class PartyCheck
    {
        private static readonly Dictionary<NetworkUser, (string Fingerprint, string Reason)> Reports = new Dictionary<NetworkUser, (string, string)>();
        private static string _lastSent = "";

        public static void Init()
        {
            NetworkingAPI.RegisterMessageType<MapStatusMessage>();
            NetworkUser.onPostNetworkUserStart += user => { if (user.isLocalPlayer) Report(user); };
            NetworkUser.onNetworkUserLost += user => Reports.Remove(user);
            RoR2Application.onUpdate += Poll;
        }

        /// <summary>This machine's status: its fingerprint (empty when it can't load the stage) and why not.</summary>
        public static (string Fingerprint, string Reason) Local()
        {
            MapConverter? c = DeadlockMaps.SunkenPlaza;
            if (c == null) return ("", "has Deadlock maps switched off");
            return c.State == MapState.Ready && c.Fingerprint != null ? (c.Fingerprint, "ready") : ("", c.State switch
            {
                MapState.NoDeadlock => "has no usable Deadlock install (" + c.Status + ")",
                MapState.Converting => "is still converting the map",
                MapState.Failed => "couldn't convert the map",
                _ => "hasn't converted the map yet",
            });
        }

        /// <summary>Resend when this machine's status changes (a conversion finishing mid-lobby).</summary>
        private static void Poll()
        {
            if (!NetworkClient.active && !NetworkServer.active) return;
            var (fp, reason) = Local();
            if (fp + reason == _lastSent) return;
            foreach (NetworkUser u in NetworkUser.readOnlyLocalPlayersList) Report(u);
        }

        private static void Report(NetworkUser user)
        {
            var (fp, reason) = Local();
            _lastSent = fp + reason;
            new MapStatusMessage { User = user.gameObject, Fingerprint = fp, Reason = reason }.Send(NetworkDestination.Server);
        }

        internal static void ServerRecord(NetworkUser user, string fingerprint, string reason)
        {
            Reports[user] = (fingerprint, reason);
            CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[DeadlockMap] {user.userName}: {(fingerprint.Length > 0 ? "ready " + fingerprint : reason)}");
        }

        /// <summary>Server: true when every player in the run reported the host's own fingerprint. Why = who isn't.</summary>
        public static bool AllReady(out string why)
        {
            var (host, hostReason) = Local();
            if (host.Length == 0) { why = "the host " + hostReason; return false; }
            foreach (NetworkUser u in NetworkUser.readOnlyInstancesList)
            {
                if (!Reports.TryGetValue(u, out var r)) { why = $"{u.userName} hasn't reported a Deadlock install"; return false; }
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
        public string Fingerprint = "";
        public string Reason = "";

        public void Serialize(NetworkWriter w) { w.Write(User); w.Write(Fingerprint); w.Write(Reason); }

        public void Deserialize(NetworkReader r) { User = r.ReadGameObject(); Fingerprint = r.ReadString(); Reason = r.ReadString(); }

        public void OnReceived()
        {
            if (!NetworkServer.active || User == null || !User.TryGetComponent(out NetworkUser user)) return;
            PartyCheck.ServerRecord(user, Fingerprint, Reason);
        }
    }
}
