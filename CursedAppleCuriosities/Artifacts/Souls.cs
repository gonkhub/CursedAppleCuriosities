using System.Text;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace CursedAppleCuriosities.Artifacts
{
    /// <summary>
    /// Artifact of the Catalog's currency. Gold stays the real money; with the artifact on, every place it shows (the HUD
    /// wallet, prices on holograms, prompts and pings) shows souls instead, at a rate fixed when each stage starts so a
    /// basic chest's gold price is exactly <see cref="PerChest"/> souls. The server sends that price (<see cref="Unit"/>)
    /// to everyone; until it arrives a client works it out itself.
    /// </summary>
    public static class Souls
    {
        public const int PerChest = 800;
        private const int BasicChestBaseCost = 25; // vanilla Chest1

        // The rate for one stage, keyed by run and stage count (a client can hear the server's before its own stage starts).
        private static int _unit, _received;
        private static (Run? run, int stage) _unitKey, _receivedKey;

        private static (Run?, int) Key => (Run.instance, Run.instance != null ? Run.instance.stageClearCount : -1);

        /// <summary>A basic chest's price in gold on this stage (souls = gold * PerChest / Unit).</summary>
        public static int Unit
        {
            get
            {
                var key = Key;
                if (!NetworkServer.active && _receivedKey == key) return _received;
                if (_unitKey != key)
                {
                    _unitKey = key;
                    _unit = Run.instance != null ? Mathf.Max(1, Run.instance.GetDifficultyScaledCost(BasicChestBaseCost)) : BasicChestBaseCost;
                }
                return _unit;
            }
        }

        public static int FromGold(long gold) => (int)Mathf.Min(int.MaxValue, Mathf.Round(gold * (float)PerChest / Unit));

        /// <summary>The gold price of a chest worth this many basic chests.</summary>
        public static int GoldFor(int chests) => Unit * chests;

        public static void Hook()
        {
            NetworkingAPI.RegisterMessageType<SoulUnitMessage>();
            // The server fixes the stage's rate as it starts and tells everyone (again to anyone who joins later).
            Stage.onServerStageBegin += _ => Send(); // the first ask of a stage fixes its rate
            NetworkUser.onPostNetworkUserStart += _ => { if (NetworkServer.active && Stage.instance != null) Send(); };

            On.RoR2.CostTypeDef.BuildCostString += (orig, self, cost, sb) =>
            {
                if (CatalogArtifact.Active && self == CostTypeCatalog.GetCostTypeDef(CostTypeIndex.Money)) { sb.Append(FromGold(cost)).Append(" Souls"); return; }
                orig(self, cost, sb);
            };
            On.RoR2.UI.HUD.Update += (orig, self) =>
            {
                orig(self);
                if (!CatalogArtifact.Active || self.moneyText == null || self.targetMaster == null) return;
                self.moneyText.targetValue = FromGold(self.targetMaster.money); // from the master each frame: never compounds
                HideDollarSign(self.moneyText);
            };
        }

        private static void Send()
        {
            if (!NetworkServer.active || !CatalogArtifact.Active) return;
            new SoulUnitMessage { Unit = Unit, Stage = Run.instance!.stageClearCount }.Send(NetworkDestination.Clients);
        }

        /// <summary>A client: the server's price for this stage.</summary>
        internal static void Receive(int unit, int stage)
        {
            if (NetworkServer.active) return;
            _receivedKey = (Run.instance, stage);
            _received = Mathf.Max(1, unit);
        }

        /// <summary>The HUD wallet's "$" becomes "Souls" after the number.</summary>
        private static void HideDollarSign(RoR2.UI.MoneyText money)
        {
            if (money.GetComponent<SoulsLabel>() != null) return;
            money.gameObject.AddComponent<SoulsLabel>();
            Transform root = money.transform.parent != null ? money.transform.parent : money.transform;
            foreach (TextMeshProUGUI t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t != money.targetText && t.text.Trim() == "$") t.text = "";
        }
    }

    /// <summary>Marks a HUD wallet already relabelled; appends " Souls" after the number each frame.</summary>
    public class SoulsLabel : MonoBehaviour
    {
        private RoR2.UI.MoneyText? _money;
        private void Awake() => _money = GetComponent<RoR2.UI.MoneyText>();
        private void LateUpdate()
        {
            if (_money == null || _money.targetText == null || !CatalogArtifact.Active) return;
            string s = _money.targetText.text;
            if (!s.EndsWith(" Souls")) _money.targetText.text = s + " Souls";
        }
    }

    public class SoulUnitMessage : INetMessage
    {
        public int Unit, Stage;
        public void Serialize(NetworkWriter w) { w.Write(Unit); w.Write(Stage); }
        public void Deserialize(NetworkReader r) { Unit = r.ReadInt32(); Stage = r.ReadInt32(); }
        public void OnReceived() => Souls.Receive(Unit, Stage);
    }
}
