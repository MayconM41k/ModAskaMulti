using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using SSSGame.Weather;
using TMPro;
using UnityEngine;

[assembly: AssemblyTitle("AskaMultipleEye")]
[assembly: AssemblyVersion("0.9.4.0")]
[assembly: AssemblyFileVersion("0.9.4.0")]

namespace AskaMultipleEye
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "AskaMultipleEye";
        public const string PluginName = "Aska Multiple Eye";
        public const string PluginVersion = "0.9.4";

        internal static ManualLogSource LoggerInstance;

        internal static ConfigEntry<bool> GeneralEnabled;
        internal static ConfigEntry<bool> AllowMultiplePerWorld;
        internal static ConfigEntry<string> ExtraNameFragments;
        internal static ConfigEntry<bool> IndependentVillages;
        internal static ConfigEntry<bool> PerVillageUnlocks;
        internal static ConfigEntry<float> MinVillageRadius;
        internal static ConfigEntry<bool> WispForEveryPlayer;
        internal static ConfigEntry<bool> ProbeEnabled;
        internal static ConfigEntry<KeyCode> DumpProbeKey;
        internal static ConfigEntry<bool> ShowPerEyeTimers;
        internal static ConfigEntry<int> MinEyesForMultiHud;
        internal static ConfigEntry<float> RefreshSeconds;
        internal static ConfigEntry<string> MessageFormat;
        internal static ConfigEntry<bool> MigratedShortMessageFormat;
        internal static ConfigEntry<bool> MigratedMinHudToOne;

        internal static bool InEyeValidation;
        private static float _lastBlockedLogTime;

        internal static readonly List<Vector3> ActiveEyePositions = new List<Vector3>();
        internal static readonly Dictionary<int, int> VillagerEyeAssignment = new Dictionary<int, int>();
        internal static readonly List<VillagerSnapshot> CachedVillagers = new List<VillagerSnapshot>();
        internal static readonly Dictionary<int, HashSet<int>> VillageBuiltTemplateIds = new Dictionary<int, HashSet<int>>();
        internal static readonly Dictionary<int, List<StructureTemplate>> VillageActiveTemplates = new Dictionary<int, List<StructureTemplate>>();
        internal static readonly Dictionary<int, List<StructureTemplate>> VillageAllTemplatesIncludingSites = new Dictionary<int, List<StructureTemplate>>();
        internal static readonly Dictionary<int, int> VillagePopulation = new Dictionary<int, int>();

        // Per-Player progression: tracking structures built by THIS player.
        internal static readonly HashSet<int> LocalPlayerBuiltTemplateIds = new HashSet<int>();
        internal static readonly HashSet<string> LocalPlayerBuiltTemplateNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        internal static readonly List<StructureTemplate> LocalPlayerBuiltTemplates = new List<StructureTemplate>();
        internal static uint LocalUserKey = 0;

        // Exactly 1 Eye of Odin per player:
        internal static bool LocalPlayerHasEye = false;
        internal static Vector3 LocalPlayerEyePosition = Vector3.zero;
        internal static int LocalPlayerEyeIndex = -1;

        internal static void RegisterLocalPlayerBuiltTemplate(StructureTemplate st)
        {
            if (st == null)
                return;

            int id = st.GetInstanceID();
            if (!LocalPlayerBuiltTemplateIds.Contains(id))
            {
                LocalPlayerBuiltTemplateIds.Add(id);
                LocalPlayerBuiltTemplates.Add(st);
            }

            try
            {
                string n = st.name;
                if (!string.IsNullOrEmpty(n) && !LocalPlayerBuiltTemplateNames.Contains(n))
                {
                    LocalPlayerBuiltTemplateNames.Add(n);
                }
            }
            catch
            {
            }
        }

        internal static bool HasPendingSpawnEye;
        internal static Vector3 PendingSpawnEyePos;

        internal struct VillagerSnapshot
        {
            public Vector3 Position;
            public int EyeIndex;
        }

        public override void Load()
        {
            LoggerInstance = Log;

            ProbeEnabled = Config.Bind<bool>(
                "Debug",
                "ProbeEnabled",
                true,
                "F8 logs settlement / Eye discovery to BepInEx (works even when Eye.AllowMultiplePerWorld is off).");

            DumpProbeKey = Config.Bind<KeyCode>(
                "Debug",
                "DumpProbeKey",
                KeyCode.F8,
                "Dump settlement / Eye state to LogOutput.log.");

            AllowMultiplePerWorld = Config.Bind<bool>(
                "Eye",
                "AllowMultiplePerWorld",
                true,
                "Allow multiple Eyes: each stays an outpost, but OVERLAPPING_OUTPOST and uniqueness checks are relaxed so you can place them side by side.");

            ExtraNameFragments = Config.Bind<string>(
                "Eye",
                "ExtraNameFragments",
                "The Eye of Odin, HearthstoneCore, SettlementCore",
                "Comma-separated substrings matched against blueprint Name (case-insensitive).");

            IndependentVillages = Config.Bind<bool>(
                "Village",
                "IndependentVillages",
                true,
                "Each Eye of Odin acts as an independent village. Villagers summoned or housed near an Eye are bound to that Eye and only use storages, food, and structures inside their own Eye's territory (no Marketplace needed).");

            PerVillageUnlocks = Config.Bind<bool>(
                "Village",
                "PerVillageUnlocks",
                true,
                "Require each Eye's village to build its own prerequisite structures and reach required population in its own territory to unlock higher-tier buildings in the build menu, and allow each village to build its own unique buildings (like Chieftain's Hut).");

            MinVillageRadius = Config.Bind<float>(
                "Village",
                "MinVillageRadius",
                45.0f,
                "Minimum territory search radius around each Eye of Odin when multiple Eyes are active.");

            WispForEveryPlayer = Config.Bind<bool>(
                "Guide",
                "WispForEveryPlayer",
                false,
                "Ensure every player (host and guests) gets their own local guiding flame (Wisdom Wisp / tutorial NPC) when spawning into the world. Disabled by default for multiplayer stability.");

            GeneralEnabled = Config.Bind<bool>(
                "General",
                "Enabled",
                true,
                "Allow placing more than one Eye (settlement core) and independent village management per Eye.");

            ShowPerEyeTimers = Config.Bind<bool>(
                "SummonHud",
                "ShowPerEyeTimers",
                true,
                "Show per-Eye villager summon timers on the right (queued HUD with Eye name). Replaces vanilla's single timer while any Eye is summoning.");

            MinEyesForMultiHud = Config.Bind<int>(
                "SummonHud",
                "MinEyesForMultiHud",
                1,
                "Stacked timer rows when this many Eyes summon at once (2+). One Eye always adds the Eye name to vanilla's timer; no need to delete config on update.");

            RefreshSeconds = Config.Bind<float>(
                "SummonHud",
                "RefreshSeconds",
                0.5f,
                "How often per-Eye summon HUD text is refreshed.");

            MessageFormat = Config.Bind<string>(
                "SummonHud",
                "MessageFormat",
                "{0}: {1}",
                "Per-Eye rows under vanilla timer: {0} = Eye name, {1} = MM:SS.");

            MigratedShortMessageFormat = Config.Bind<bool>(
                "SummonHud",
                "MigratedShortMessageFormat",
                true,
                "Internal: one-time bump from long 'Time until the next villager...' wording to short Eye: time.");

            MigratedMinHudToOne = Config.Bind<bool>(
                "SummonHud",
                "MigratedMinHudToOne",
                false,
                "Internal flag: legacy installs with MinEyesForMultiHud=2 are bumped to 1 once so a single Eye summon still shows the Eye name.");

            if (!MigratedMinHudToOne.Value && MinEyesForMultiHud.Value == 2)
            {
                MinEyesForMultiHud.Value = 1;
                MigratedMinHudToOne.Value = true;
            }

            AddComponent<MultipleEyeHost>();

            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo(string.Format("{0} {1} loaded — Multi-Eye + Independent Villages + Per-Village Unlocks active.", PluginName, PluginVersion));
            Log.LogInfo(string.Format("[MultipleEye] allowMultiple={0} | independentVillages={1} | perVillageUnlocks={2}",
                AllowMultiplePerWorld.Value, IndependentVillages.Value, PerVillageUnlocks.Value));
        }

        internal static bool IsModActive()
        {
            return GeneralEnabled != null && GeneralEnabled.Value &&
                   AllowMultiplePerWorld != null && AllowMultiplePerWorld.Value;
        }

        internal static bool IsIndependentVillagesActive()
        {
            return IsModActive() &&
                   IndependentVillages != null && IndependentVillages.Value &&
                   ActiveEyePositions.Count >= 2;
        }

        // Progress is PER PLAYER: active from game start (even on the beach before any Eye is placed).
        internal static bool IsPerPlayerProgressionActive()
        {
            return IsModActive() &&
                   PerVillageUnlocks != null && PerVillageUnlocks.Value;
        }

        internal static bool IsPerVillageUnlocksActive()
        {
            return IsPerPlayerProgressionActive();
        }

        internal static readonly List<string> ActiveEyeOwners = new List<string>();
        internal static string LocalUserId = string.Empty;
        internal static int CachedLocalVillage = -1;

        internal static string GetLocalUserIdSafe()
        {
            try
            {
                SandSailorStudio.Platform.PlatformManager pm = SandSailorStudio.Platform.PlatformManager.Instance;
                if (pm != null && pm.Users != null)
                {
                    SandSailorStudio.Platform.PlatformUsers.User u = pm.Users.GetActiveUser();
                    if (u != null && !string.IsNullOrEmpty(u.ID))
                        return u.ID;
                }
            }
            catch
            {
            }

            try
            {
                PlayerManager pm = UnityEngine.Object.FindObjectOfType<PlayerManager>();
                if (pm != null && pm.LocalPlayer != null && pm.LocalPlayer.user != null)
                {
                    string id = pm.LocalPlayer.user.ID;
                    if (!string.IsNullOrEmpty(id))
                        return id;
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        internal static string GetCreatorUserIdSafe(Structure s, UserDataManager udm)
        {
            if (s == null || udm == null)
                return string.Empty;
            try
            {
                uint key = s.NetworkedCreatorUserId;
                if (key == 0)
                    return string.Empty;
                string id = udm.GetUserId(key);
                return id != null ? id : string.Empty;
            }
            catch
            {
            }
            return string.Empty;
        }

        internal static int ComputeLocalPlayerVillageIndex()
        {
            int count = ActiveEyePositions.Count;
            if (count == 0)
            {
                LocalPlayerHasEye = false;
                LocalPlayerEyeIndex = -1;
                return -1;
            }

            // 1) Match by owner user ID
            if (!string.IsNullOrEmpty(LocalUserId))
            {
                for (int i = 0; i < count; i++)
                {
                    if (ActiveEyeOwners.Count > i && ActiveEyeOwners[i] == LocalUserId)
                    {
                        LocalPlayerHasEye = true;
                        LocalPlayerEyePosition = ActiveEyePositions[i];
                        LocalPlayerEyeIndex = i;
                        return i;
                    }
                }
            }

            // 2) Match by locally placed Eye position
            if (LocalPlayerEyePosition != Vector3.zero)
            {
                for (int i = 0; i < count; i++)
                {
                    if (HorizontalDistSq(ActiveEyePositions[i], LocalPlayerEyePosition) < 9.0f)
                    {
                        LocalPlayerHasEye = true;
                        LocalPlayerEyeIndex = i;
                        return i;
                    }
                }
            }

            // 3) Match by local structure placement flag on Eye outlets
            try
            {
                Il2CppArrayBase<VillagerOutlet> outlets = UnityEngine.Object.FindObjectsOfType<VillagerOutlet>();
                if (outlets != null)
                {
                    for (int i = 0; i < outlets.Length; i++)
                    {
                        VillagerOutlet vo = outlets[i];
                        if (vo != null && vo.OwnerStructure != null)
                        {
                            bool isMine = false;
                            try { isMine = vo.OwnerStructure.LocallyPlacedStructure; } catch { }
                            if (!isMine && LocalUserKey != 0 && vo.OwnerStructure.NetworkedCreatorUserId == LocalUserKey)
                                isMine = true;

                            if (isMine)
                            {
                                Vector3 p = vo.OwnerStructure.transform.position;
                                int idx = GetClosestEyeIndex(p);
                                if (idx >= 0)
                                {
                                    LocalPlayerHasEye = true;
                                    LocalPlayerEyePosition = p;
                                    LocalPlayerEyeIndex = idx;
                                    return idx;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // 4) This player does NOT own any Eye of Odin in the world yet.
            LocalPlayerHasEye = false;
            LocalPlayerEyeIndex = -1;
            return -1;
        }

        internal static float HorizontalDistSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        internal static float HorizontalDist(Vector3 a, Vector3 b)
        {
            return Mathf.Sqrt(HorizontalDistSq(a, b));
        }

        internal static Vector3 GetPlacementPosition(Placement placement)
        {
            if (placement == null)
                return Vector3.zero;
            try
            {
                Vector3 p = placement.GetPivotPosition();
                if (p != Vector3.zero)
                    return p;
            }
            catch { }
            try
            {
                if (placement.PlacementObject != null && placement.PlacementObject.transform != null)
                    return placement.PlacementObject.transform.position;
            }
            catch { }
            return Vector3.zero;
        }

        internal static int GetClosestEyeIndex(Vector3 pos)
        {
            int count = ActiveEyePositions.Count;
            if (count == 0)
                return -1;
            if (count == 1)
                return 0;

            int bestIndex = 0;
            float bestDistSq = HorizontalDistSq(pos, ActiveEyePositions[0]);
            for (int i = 1; i < count; i++)
            {
                float dSq = HorizontalDistSq(pos, ActiveEyePositions[i]);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        internal static int GetLocalPlayerVillageIndex()
        {
            return CachedLocalVillage;
        }

        internal static int GetVillagePopulation(int eyeIndex)
        {
            int pop;
            if (VillagePopulation.TryGetValue(eyeIndex, out pop))
                return pop;
            return 0;
        }

        // Checks if THIS player built the requested template.
        internal static bool IsTemplateBuiltByLocalPlayer(StructureTemplate reqTemplate, bool includeUpgrades)
        {
            if (reqTemplate == null)
                return true;

            if (IsEyeTemplate(reqTemplate))
                return true;

            if (!IsPerPlayerProgressionActive())
                return true;

            try
            {
                string reqName = reqTemplate.name;
                if (!string.IsNullOrEmpty(reqName) && LocalPlayerBuiltTemplateNames.Contains(reqName))
                    return true;
            }
            catch
            {
            }

            int reqId = reqTemplate.GetInstanceID();
            if (LocalPlayerBuiltTemplateIds.Contains(reqId))
                return true;

            if (includeUpgrades)
            {
                for (int i = 0; i < LocalPlayerBuiltTemplates.Count; i++)
                {
                    StructureTemplate st = LocalPlayerBuiltTemplates[i];
                    if (st == null)
                        continue;

                    if (st.Pointer == reqTemplate.Pointer || st.GetInstanceID() == reqId)
                        return true;

                    try
                    {
                        if (string.Equals(st.name, reqTemplate.name, System.StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (reqTemplate.HasUpgrade(st, true))
                            return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        internal static bool AreRequirementsMetByLocalPlayer(NodeTemplate node)
        {
            if (node == null || !IsPerPlayerProgressionActive())
                return true;

            if (IsEyeTemplate(node))
                return true;

            try
            {
                Il2CppSystem.Collections.Generic.List<BlueprintConditionsRule> rules = node.blueprintConditionsRules;
                if (rules != null && rules.Count > 0)
                {
                    for (int i = 0; i < rules.Count; i++)
                    {
                        BlueprintConditionsRule rule = rules[i];
                        if (rule == null)
                            continue;

                        Il2CppSystem.Collections.Generic.List<StructureTemplate> reqStructures = rule.RequiredStructures;
                        if (reqStructures != null && reqStructures.Count > 0)
                        {
                            for (int j = 0; j < reqStructures.Count; j++)
                            {
                                StructureTemplate req = reqStructures[j];
                                if (req == null || IsEyeTemplate(req))
                                    continue;

                                if (!IsTemplateBuiltByLocalPlayer(req, true))
                                    return false;
                            }
                        }

                        int reqPop = rule.RequiredPopulation;
                        if (reqPop > 0)
                        {
                            if (!LocalPlayerHasEye || LocalPlayerEyeIndex < 0)
                                return false;

                            if (GetVillagePopulation(LocalPlayerEyeIndex) < reqPop)
                                return false;
                        }
                    }
                }
            }
            catch
            {
            }

            return true;
        }

        internal static bool IsTemplateUnlockedForLocalPlayer(StructureTemplate template)
        {
            if (template == null || !IsPerPlayerProgressionActive())
                return true;

            if (IsEyeTemplate(template))
                return true;

            try
            {
                NodeTemplate node = template.TryCast<NodeTemplate>();
                if (node != null)
                {
                    return AreRequirementsMetByLocalPlayer(node);
                }
            }
            catch
            {
            }

            return true;
        }

        internal static bool IsTemplateBuiltInVillage(StructureTemplate reqTemplate, int eyeIndex, bool includeUnbuiltSites)
        {
            return IsTemplateBuiltByLocalPlayer(reqTemplate, true);
        }

        internal static bool IsNodeUnlockedInVillage(NodeTemplate node, int eyeIndex)
        {
            return AreRequirementsMetByLocalPlayer(node);
        }

        internal static bool IsTemplateUnlockedInVillage(StructureTemplate template, int eyeIndex)
        {
            return IsTemplateUnlockedForLocalPlayer(template);
        }

        internal static int ResolveOwnerEyeIndex(Vector3 agentPos)
        {
            int vCount = CachedVillagers.Count;
            for (int i = 0; i < vCount; i++)
            {
                if (HorizontalDistSq(agentPos, CachedVillagers[i].Position) <= 4.0f)
                {
                    int assigned = CachedVillagers[i].EyeIndex;
                    if (assigned >= 0 && assigned < ActiveEyePositions.Count)
                        return assigned;
                }
            }
            return GetClosestEyeIndex(agentPos);
        }

        internal static int GetVillagerVillageIndex(Villager v)
        {
            if (v == null)
                return -1;

            int vId = v.PersistentUniqueID;
            if (vId != 0)
            {
                int assigned;
                if (VillagerEyeAssignment.TryGetValue(vId, out assigned))
                    return assigned;
            }

            int instId = v.GetInstanceID();
            int instAssigned;
            if (VillagerEyeAssignment.TryGetValue(instId, out instAssigned))
                return instAssigned;

            try
            {
                if (v.HasHome())
                {
                    Homestead home = v.GetHomestead();
                    if (home != null && home.transform != null)
                        return GetClosestEyeIndex(home.transform.position);
                }
            }
            catch
            {
            }

            try
            {
                Vector3 homeOrig = v.HomeOrigin;
                if (homeOrig.sqrMagnitude > 1.0f)
                    return GetClosestEyeIndex(homeOrig);
            }
            catch
            {
            }

            try
            {
                if (v.transform != null)
                    return GetClosestEyeIndex(v.transform.position);
            }
            catch
            {
            }

            return -1;
        }

        internal static bool IsVillagerInLocalPlayerVillage(Villager v)
        {
            if (v == null || !IsIndependentVillagesActive())
                return true;

            // If this player does not own an Eye of Odin, they have no villagers.
            if (!LocalPlayerHasEye || LocalPlayerEyeIndex < 0)
                return false;

            int vEye = GetVillagerVillageIndex(v);
            if (vEye < 0)
            {
                if (v.transform != null && LocalPlayerEyePosition != Vector3.zero)
                {
                    int closest = GetClosestEyeIndex(v.transform.position);
                    return closest == LocalPlayerEyeIndex;
                }
                return false;
            }

            return vEye == LocalPlayerEyeIndex;
        }

        internal static Villager GetFirstLocalVillageVillager()
        {
            if (!LocalPlayerHasEye || LocalPlayerEyeIndex < 0)
                return null;

            try
            {
                Il2CppArrayBase<Villager> all = UnityEngine.Object.FindObjectsOfType<Villager>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        Villager v = all[i];
                        if (v != null && IsVillagerInLocalPlayerVillage(v))
                            return v;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        internal static bool ArePositionsInSameVillage(Vector3 callerPos, Vector3 targetPos)
        {
            if (!IsIndependentVillagesActive())
                return true;

            int callerEye = ResolveOwnerEyeIndex(callerPos);
            int targetEye = GetClosestEyeIndex(targetPos);
            if (callerEye < 0 || targetEye < 0)
                return true;

            return callerEye == targetEye;
        }

        internal static bool IsComponentInSameVillage(Vector3 callerPos, Il2CppObjectBase targetObj)
        {
            if (targetObj == null || !IsIndependentVillagesActive())
                return true;

            try
            {
                Component comp = targetObj.TryCast<Component>();
                if (comp != null && comp.transform != null)
                {
                    return ArePositionsInSameVillage(callerPos, comp.transform.position);
                }
            }
            catch
            {
            }
            return true;
        }

        internal static void RestrictRangeToVillage(ref Vector3 searchOrigin, ref float maxRange, bool allowRecenterOrigin)
        {
            if (!IsIndependentVillagesActive())
                return;

            int eyeIndex = ResolveOwnerEyeIndex(searchOrigin);
            if (eyeIndex < 0 || eyeIndex >= ActiveEyePositions.Count)
                return;

            Vector3 myEye = ActiveEyePositions[eyeIndex];
            float minOtherEyeDist = float.MaxValue;
            float minBorderDistFromOrigin = float.MaxValue;

            for (int j = 0; j < ActiveEyePositions.Count; j++)
            {
                if (j == eyeIndex)
                    continue;

                Vector3 otherEye = ActiveEyePositions[j];
                float dx = otherEye.x - myEye.x;
                float dz = otherEye.z - myEye.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < 1.0f)
                    continue;

                if (dist < minOtherEyeDist)
                    minOtherEyeDist = dist;

                float ux = dx / dist;
                float uz = dz / dist;
                float proj = (searchOrigin.x - myEye.x) * ux + (searchOrigin.z - myEye.z) * uz;
                float borderDist = (dist * 0.5f) - proj;
                if (borderDist < minBorderDistFromOrigin)
                    minBorderDistFromOrigin = borderDist;
            }

            if (minOtherEyeDist >= float.MaxValue * 0.5f)
                return;

            float minFloor = MinVillageRadius != null ? Mathf.Max(20.0f, MinVillageRadius.Value) : 45.0f;
            float safeVillageRadius = Mathf.Max(minFloor, minOtherEyeDist * 0.47f);
            float safeFromOrigin = minBorderDistFromOrigin * 0.95f;

            if (maxRange <= safeFromOrigin && safeFromOrigin > 10.0f)
                return;

            if (allowRecenterOrigin && safeFromOrigin < safeVillageRadius)
            {
                searchOrigin = myEye;
                if (maxRange > safeVillageRadius)
                    maxRange = safeVillageRadius;
            }
            else
            {
                float cap = Mathf.Max(minFloor, safeFromOrigin);
                if (maxRange > cap)
                    maxRange = cap;
            }
        }

        internal static bool IsEyeTemplate(StructureTemplate template)
        {
            if (template == null)
                return false;

            try
            {
                if (SettlementIssueTrackerWidget.Instance != null &&
                    SettlementIssueTrackerWidget.Instance.coreTemplate != null &&
                    SettlementIssueTrackerWidget.Instance.coreTemplate.Pointer == template.Pointer)
                {
                    return true;
                }

                string objName = template.name != null ? template.name : string.Empty;
                string bpName = string.Empty;
                try { bpName = template.Name != null ? template.Name : string.Empty; } catch { }

                if (MatchesEyeName(objName) || MatchesEyeName(bpName))
                    return true;

                NodeTemplate nodeTemplate = template.TryCast<NodeTemplate>();
                if (nodeTemplate != null && nodeTemplate.nodeStructure != null)
                {
                    Structure ns = nodeTemplate.nodeStructure;
                    if (ns.TryCast<CoreStructure>() != null)
                        return true;
                    if (ns.GetComponent<CoreStructure>() != null || ns.GetComponentInChildren<VillagerOutlet>(true) != null)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal static bool MatchesEyeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.IndexOf("Eye of Odin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("EyeOfOdin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("HearthstoneCore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("SettlementCore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Structure_Core", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string rawFragments = ExtraNameFragments != null ? ExtraNameFragments.Value : string.Empty;
            if (!string.IsNullOrWhiteSpace(rawFragments))
            {
                string[] parts = rawFragments.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string frag = parts[i].Trim();
                    if (frag.Length > 0 && value.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return false;
        }

        internal static void RelaxEyeTemplate(StructureTemplate template)
        {
            if (template == null || !IsModActive())
                return;

            try
            {
                template.uniqueBuilding = false;
                template.settlementRequirement = StructureTemplate.SettlementRequirement.none;
                template.outpostRequirement = StructureTemplate.OutpostRequirement.none;
            }
            catch
            {
            }
        }

        internal static bool IsRelaxableValidity(PlacementValidity validity)
        {
            return validity == PlacementValidity.OVERLAPPING_OUTPOST ||
                   validity == PlacementValidity.UNIQUE_AND_ALREADY_PRESENT ||
                   validity == PlacementValidity.OUTSIDE_SETTLEMENT ||
                   validity == PlacementValidity.INSIDE_SETTLEMENT;
        }

        internal static void LogBlockedIfEnabled(string source, PlacementValidity validity)
        {
            if (ProbeEnabled == null || !ProbeEnabled.Value)
                return;
            if (validity == PlacementValidity.VALID)
                return;

            float now = Time.unscaledTime;
            if (now - _lastBlockedLogTime >= 2.0f)
            {
                _lastBlockedLogTime = now;
                if (LoggerInstance != null)
                {
                    LoggerInstance.LogInfo(string.Format("[MultipleEye] Blocked Eye placement at {0}: validity={1} ({2})", source, validity, (int)validity));
                }
            }
        }
    }

    public class MultipleEyeHost : MonoBehaviour
    {
        private float _nextRefreshTime;
        private float _nextTemplateScanTime;
        private float _nextVillageSyncTime;
        private string _cachedHudText = string.Empty;

        public MultipleEyeHost(IntPtr ptr) : base(ptr)
        {
        }

        private void Update()
        {
            try
            {
                if (Plugin.ProbeEnabled != null && Plugin.ProbeEnabled.Value &&
                    Plugin.DumpProbeKey != null && Plugin.DumpProbeKey.Value != KeyCode.None &&
                    Input.GetKeyDown(Plugin.DumpProbeKey.Value))
                {
                    DumpProbeState();
                }

                float now = Time.unscaledTime;
                if (Plugin.IsModActive() && now >= _nextTemplateScanTime)
                {
                    _nextTemplateScanTime = now + 2.0f;
                    RelaxKnownTemplates();
                }

                if (Plugin.IsModActive() && now >= _nextVillageSyncTime)
                {
                    _nextVillageSyncTime = now + 1.0f;
                    SyncIndependentVillages();
                }

                if (Plugin.ShowPerEyeTimers != null && Plugin.ShowPerEyeTimers.Value)
                {
                    float interval = Plugin.RefreshSeconds != null ? Mathf.Max(0.1f, Plugin.RefreshSeconds.Value) : 0.5f;
                    if (now >= _nextRefreshTime)
                    {
                        _nextRefreshTime = now + interval;
                        RefreshSummonHudText();
                    }
                    ApplySummonHudOverlay();
                }
            }
            catch
            {
            }
        }

        internal static void SyncIndependentVillages()
        {
            try
            {
                Plugin.ActiveEyePositions.Clear();
                Plugin.ActiveEyeOwners.Clear();
                if (string.IsNullOrEmpty(Plugin.LocalUserId))
                    Plugin.LocalUserId = Plugin.GetLocalUserIdSafe();

                UserDataManager udm = null;
                try { udm = UnityEngine.Object.FindObjectOfType<UserDataManager>(); } catch { }

                List<Vector3> eyePos = new List<Vector3>();
                List<string> eyeOwner = new List<string>();
                Il2CppArrayBase<VillagerOutlet> outlets = UnityEngine.Object.FindObjectsOfType<VillagerOutlet>();
                if (outlets != null)
                {
                    for (int i = 0; i < outlets.Length; i++)
                    {
                        VillagerOutlet vo = outlets[i];
                        if (vo == null || vo.transform == null)
                            continue;

                        Vector3 pos = vo.transform.position;
                        string owner = string.Empty;
                        if (vo.OwnerStructure != null && vo.OwnerStructure.transform != null)
                        {
                            pos = vo.OwnerStructure.transform.position;
                            owner = Plugin.GetCreatorUserIdSafe(vo.OwnerStructure, udm);
                        }

                        bool duplicate = false;
                        for (int j = 0; j < eyePos.Count; j++)
                        {
                            if (Plugin.HorizontalDistSq(pos, eyePos[j]) < 4.0f)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                        if (!duplicate)
                        {
                            eyePos.Add(pos);
                            eyeOwner.Add(owner);
                        }
                    }
                }

                // Deterministic order so village indices never swap between scans.
                while (eyePos.Count > 0)
                {
                    int pick = 0;
                    for (int j = 1; j < eyePos.Count; j++)
                    {
                        if (eyePos[j].x < eyePos[pick].x ||
                            (Mathf.Approximately(eyePos[j].x, eyePos[pick].x) && eyePos[j].z < eyePos[pick].z))
                            pick = j;
                    }
                    Plugin.ActiveEyePositions.Add(eyePos[pick]);
                    Plugin.ActiveEyeOwners.Add(eyeOwner[pick]);
                    eyePos.RemoveAt(pick);
                    eyeOwner.RemoveAt(pick);
                }

                Plugin.CachedLocalVillage = Plugin.ComputeLocalPlayerVillageIndex();
                Plugin.VillageBuiltTemplateIds.Clear();

                Plugin.CachedVillagers.Clear();
                Plugin.VillagePopulation.Clear();
                Plugin.VillageActiveTemplates.Clear();
                Plugin.VillageAllTemplatesIncludingSites.Clear();

                int eyeCount = Plugin.ActiveEyePositions.Count;
                for (int e = 0; e < eyeCount; e++)
                {
                    Plugin.VillagePopulation[e] = 0;
                    Plugin.VillageActiveTemplates[e] = new List<StructureTemplate>();
                    Plugin.VillageAllTemplatesIncludingSites[e] = new List<StructureTemplate>();
                    if (!Plugin.VillageBuiltTemplateIds.ContainsKey(e))
                    {
                        Plugin.VillageBuiltTemplateIds[e] = new HashSet<int>();
                    }
                }

                if (string.IsNullOrEmpty(Plugin.LocalUserId))
                    Plugin.LocalUserId = Plugin.GetLocalUserIdSafe();

                if (udm != null && !string.IsNullOrEmpty(Plugin.LocalUserId) && Plugin.LocalUserKey == 0)
                {
                    uint k;
                    if (udm.TryGetUserKey(Plugin.LocalUserId, out k))
                        Plugin.LocalUserKey = k;
                }

                Il2CppArrayBase<Structure> structures = UnityEngine.Object.FindObjectsOfType<Structure>();
                if (structures != null)
                {
                    for (int i = 0; i < structures.Length; i++)
                    {
                        Structure s = structures[i];
                        if (s == null || s.transform == null)
                            continue;

                        StructureTemplate st = null;
                        try { st = s.Template; } catch { }
                        if (st == null)
                            continue;

                        bool isBuiltAndActive = false;
                        try
                        {
                            isBuiltAndActive = s.IsActive && s.buildSite == null;
                        }
                        catch
                        {
                            try { isBuiltAndActive = s.IsActive; } catch { }
                        }

                        if (isBuiltAndActive)
                        {
                            bool isMine = false;
                            try
                            {
                                if (s.LocallyPlacedStructure)
                                    isMine = true;
                            }
                            catch
                            {
                            }

                            if (!isMine && Plugin.LocalUserKey != 0 && s.NetworkedCreatorUserId == Plugin.LocalUserKey)
                            {
                                isMine = true;
                            }
                            else if (!isMine && !string.IsNullOrEmpty(Plugin.LocalUserId))
                            {
                                string creator = Plugin.GetCreatorUserIdSafe(s, udm);
                                if (creator == Plugin.LocalUserId)
                                    isMine = true;
                            }

                            if (isMine)
                            {
                                Plugin.RegisterLocalPlayerBuiltTemplate(st);
                            }
                        }

                        if (eyeCount >= 1)
                        {
                            int sEye = Plugin.GetClosestEyeIndex(s.transform.position);
                            if (sEye >= 0 && sEye < eyeCount)
                            {
                                Plugin.VillageAllTemplatesIncludingSites[sEye].Add(st);
                                if (isBuiltAndActive)
                                {
                                    Plugin.VillageActiveTemplates[sEye].Add(st);
                                    try
                                    {
                                        Plugin.VillageBuiltTemplateIds[sEye].Add(st.GetInstanceID());
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }

                Il2CppArrayBase<Villager> villagers = UnityEngine.Object.FindObjectsOfType<Villager>();
                if (villagers == null)
                    return;

                for (int i = 0; i < villagers.Length; i++)
                {
                    Villager v = villagers[i];
                    if (v == null || v.transform == null)
                        continue;

                    int vId = v.PersistentUniqueID;
                    if (vId == 0)
                        vId = v.GetInstanceID();

                    int eyeIdx = -1;

                    try
                    {
                        if (v.HasHome())
                        {
                            Homestead home = v.GetHomestead();
                            if (home != null && home.transform != null)
                            {
                                eyeIdx = Plugin.GetClosestEyeIndex(home.transform.position);
                            }
                        }
                    }
                    catch { }

                    if (eyeIdx < 0)
                    {
                        try
                        {
                            Workstation ws = v.GetNonVikingWorkstation();
                            if (ws != null && ws.transform != null)
                            {
                                eyeIdx = Plugin.GetClosestEyeIndex(ws.transform.position);
                            }
                        }
                        catch { }
                    }

                    if (eyeIdx < 0)
                    {
                        int savedEye;
                        if (Plugin.VillagerEyeAssignment.TryGetValue(vId, out savedEye) &&
                            savedEye >= 0 && savedEye < eyeCount)
                        {
                            eyeIdx = savedEye;
                        }
                        else
                        {
                            Vector3 homeOrig = v.HomeOrigin;
                            if (homeOrig.sqrMagnitude > 1.0f && Plugin.GetClosestEyeIndex(homeOrig) >= 0)
                            {
                                eyeIdx = Plugin.GetClosestEyeIndex(homeOrig);
                            }
                            else
                            {
                                eyeIdx = Plugin.GetClosestEyeIndex(v.transform.position);
                            }
                        }
                    }

                    if (eyeIdx >= 0 && eyeIdx < eyeCount)
                    {
                        Plugin.VillagerEyeAssignment[vId] = eyeIdx;
                        Plugin.VillagePopulation[eyeIdx] = Plugin.GetVillagePopulation(eyeIdx) + 1;

                        bool canModifyState = true;
                        try
                        {
                            if (v.Object != null && !v.Object.HasStateAuthority)
                                canModifyState = false;
                        }
                        catch { }

                        if (canModifyState && eyeCount > 1)
                        {
                            Vector3 targetEyePos = Plugin.ActiveEyePositions[eyeIdx];
                            if (Plugin.HorizontalDistSq(v.HomeOrigin, targetEyePos) > 9.0f)
                            {
                                v.HomeOrigin = targetEyePos;
                            }
                        }

                        Plugin.VillagerSnapshot snap = new Plugin.VillagerSnapshot();
                        snap.Position = v.transform.position;
                        snap.EyeIndex = eyeIdx;
                        Plugin.CachedVillagers.Add(snap);
                    }
                }
            }
            catch
            {
            }
        }

        private void RelaxKnownTemplates()
        {
            try
            {
                if (SettlementIssueTrackerWidget.Instance != null && SettlementIssueTrackerWidget.Instance.coreTemplate != null)
                {
                    Plugin.RelaxEyeTemplate(SettlementIssueTrackerWidget.Instance.coreTemplate);
                }

                Il2CppArrayBase<PlayerBuilder> builders = UnityEngine.Object.FindObjectsOfType<PlayerBuilder>();
                if (builders != null)
                {
                    for (int i = 0; i < builders.Length; i++)
                    {
                        PlayerBuilder pb = builders[i];
                        if (pb != null && pb.StructureTemplate != null && Plugin.IsEyeTemplate(pb.StructureTemplate))
                        {
                            Plugin.RelaxEyeTemplate(pb.StructureTemplate);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void RefreshSummonHudText()
        {
            int activeCount;
            _cachedHudText = BuildSummonHudText(out activeCount);
            if (activeCount < (Plugin.MinEyesForMultiHud != null ? Plugin.MinEyesForMultiHud.Value : 1))
            {
                _cachedHudText = string.Empty;
            }
        }

        internal static string BuildSummonHudText(out int summoningCount)
        {
            summoningCount = 0;
            Il2CppArrayBase<VillagerOutlet> outlets = UnityEngine.Object.FindObjectsOfType<VillagerOutlet>();
            if (outlets == null || outlets.Length == 0)
                return string.Empty;

            WeatherSystem ws = WeatherSystem.Instance;
            string format = Plugin.MessageFormat != null && !string.IsNullOrWhiteSpace(Plugin.MessageFormat.Value)
                ? Plugin.MessageFormat.Value
                : "{0}: {1}";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < outlets.Length; i++)
            {
                VillagerOutlet outlet = outlets[i];
                if (outlet == null)
                    continue;

                // Per-player HUD: only display summon timer for the local player's own Eye
                Vector3 outPos = outlet.transform.position;
                if (outlet.OwnerStructure != null && outlet.OwnerStructure.transform != null)
                {
                    outPos = outlet.OwnerStructure.transform.position;
                }
                int outEyeIdx = Plugin.GetClosestEyeIndex(outPos);
                if (Plugin.LocalPlayerEyeIndex < 0 || outEyeIdx != Plugin.LocalPlayerEyeIndex)
                    continue;

                bool pending = false;
                try { pending = outlet._SpawnPending; } catch { }
                if (!pending && !outlet.IsActive)
                    continue;

                float endGameTime = outlet._NetworkedVillagerTimerEnd;
                if (endGameTime <= 0f)
                    continue;

                float remainingSec = 0f;
                if (ws != null)
                {
                    try
                    {
                        remainingSec = ws.GetTimeDifferenceFromCurrentGameTimeInSeconds(endGameTime);
                    }
                    catch
                    {
                    }
                }

                if (remainingSec <= 0.5f)
                    continue;

                int totalSecs = Mathf.Max(0, Mathf.FloorToInt(remainingSec));
                int mins = totalSecs / 60;
                int secs = totalSecs % 60;
                string timeFormatted = string.Format("{0:00}:{1:00}", mins, secs);

                string eyeName = GetEyeDisplayName(outlet, i + 1);
                string line;
                try
                {
                    line = string.Format(format, eyeName, timeFormatted);
                }
                catch
                {
                    line = string.Format("{0}: {1}", eyeName, timeFormatted);
                }

                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(line);
                summoningCount++;
            }

            return sb.ToString();
        }

        private static string GetEyeDisplayName(VillagerOutlet outlet, int index)
        {
            try
            {
                Structure owner = outlet.OwnerStructure;
                if (owner != null)
                {
                    CoreStructure core = owner.TryCast<CoreStructure>();
                    if (core != null && !string.IsNullOrWhiteSpace(core.OutpostName))
                        return core.OutpostName;

                    OutpostStructure outpost = owner.TryCast<OutpostStructure>();
                    if (outpost != null && !string.IsNullOrWhiteSpace(outpost.OutpostName))
                        return outpost.OutpostName;

                    if (!string.IsNullOrWhiteSpace(owner.StructureName))
                        return owner.StructureName;

                    string custom = owner.GetCustomName();
                    if (!string.IsNullOrWhiteSpace(custom))
                        return custom;

                    string name = owner.GetName();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
            catch
            {
            }

            return string.Format("Eye #{0}", index);
        }

        internal void ApplySummonHudOverlay()
        {
            if (string.IsNullOrEmpty(_cachedHudText))
                return;

            Il2CppArrayBase<WeatherWidget> widgets = UnityEngine.Object.FindObjectsOfType<WeatherWidget>();
            if (widgets == null)
                return;

            for (int i = 0; i < widgets.Length; i++)
            {
                WeatherWidget ww = widgets[i];
                if (ww == null)
                    continue;

                if (ww.villagerEtaPanel != null && !ww.villagerEtaPanel.activeSelf)
                {
                    ww.villagerEtaPanel.SetActive(true);
                }

                if (ww.villagerEta != null && ww.villagerEta.text != _cachedHudText)
                {
                    ww.villagerEta.text = _cachedHudText;
                }
            }
        }

        private void DumpProbeState()
        {
            if (Plugin.LoggerInstance != null)
                Plugin.LoggerInstance.LogInfo("=== [MultipleEye] F8 Probe Dump ===");

            try
            {
                SyncIndependentVillages();
                if (Plugin.LoggerInstance != null)
                {
                    Plugin.LoggerInstance.LogInfo(string.Format("Active Eye Villages: {0} | IndependentVillages={1} | PerVillageUnlocks={2} | LocalPlayerVillage={3}",
                        Plugin.ActiveEyePositions.Count, Plugin.IsIndependentVillagesActive(), Plugin.IsPerVillageUnlocksActive(), Plugin.GetLocalPlayerVillageIndex()));
                    for (int i = 0; i < Plugin.ActiveEyePositions.Count; i++)
                    {
                        int builtCount = Plugin.VillageActiveTemplates.ContainsKey(i) ? Plugin.VillageActiveTemplates[i].Count : 0;
                        string owner = i < Plugin.ActiveEyeOwners.Count ? Plugin.ActiveEyeOwners[i] : string.Empty;
                        Plugin.LoggerInstance.LogInfo(string.Format("  Village[{0}] EyePos={1} owner='{2}' mine={3} villagers={4} activeStructures={5}",
                            i, Plugin.ActiveEyePositions[i], owner, owner == Plugin.LocalUserId && owner.Length > 0, Plugin.GetVillagePopulation(i), builtCount));
                    }
                    Plugin.LoggerInstance.LogInfo(string.Format("  LocalUserId='{0}' (key={1}) builtCount={2} localVillage={3}",
                        Plugin.LocalUserId, Plugin.LocalUserKey, Plugin.LocalPlayerBuiltTemplates.Count, Plugin.CachedLocalVillage));
                }
            }
            catch (Exception ex)
            {
                if (Plugin.LoggerInstance != null)
                    Plugin.LoggerInstance.LogWarning(string.Format("[MultipleEye] Probe error: {0}", ex.Message));
            }
        }
    }

    [HarmonyPatch(typeof(PlayerManager), "SpawnPlayer")]
    internal static class Patch_PlayerManager_SpawnPlayer_Wisp
    {
        private static IntPtr _forcedForManager = IntPtr.Zero;

        private static void Postfix(PlayerManager __instance)
        {
            if (__instance == null || !Plugin.IsModActive())
                return;
            if (Plugin.WispForEveryPlayer == null || !Plugin.WispForEveryPlayer.Value)
                return;
            if (_forcedForManager == __instance.Pointer)
                return;

            try
            {
                _forcedForManager = __instance.Pointer;
                if (__instance._tutorialNPCInstance == null && __instance.tutorialNPC != null)
                {
                    __instance.SpawnTutorialNPC(true);
                    if (Plugin.LoggerInstance != null)
                        Plugin.LoggerInstance.LogInfo("[MultipleEye] Chama guia (Wisdom Wisp) criada para o jogador local.");
                }
            }
            catch (Exception ex)
            {
                if (Plugin.LoggerInstance != null)
                    Plugin.LoggerInstance.LogWarning(string.Format("[MultipleEye] Falha ao criar chama guia: {0}", ex.Message));
            }
        }
    }

    // Immediate registration of structures completed by the local player
    [HarmonyPatch(typeof(Settlement), "ActivateStructure")]
    internal static class Patch_Settlement_ActivateStructure
    {
        private static void Postfix(Structure s)
        {
            if (s == null || !Plugin.IsPerPlayerProgressionActive())
                return;

            try
            {
                StructureTemplate st = s.Template;
                if (st == null || Plugin.IsEyeTemplate(st))
                    return;

                bool isMine = false;
                try
                {
                    if (s.LocallyPlacedStructure)
                        isMine = true;
                }
                catch
                {
                }

                if (!isMine && Plugin.LocalUserKey != 0 && s.NetworkedCreatorUserId == Plugin.LocalUserKey)
                {
                    isMine = true;
                }
                else if (!isMine && !string.IsNullOrEmpty(Plugin.LocalUserId))
                {
                    UserDataManager udm = null;
                    try { udm = UnityEngine.Object.FindObjectOfType<UserDataManager>(); } catch { }
                    string creator = Plugin.GetCreatorUserIdSafe(s, udm);
                    if (creator == Plugin.LocalUserId)
                        isMine = true;
                }

                if (isMine)
                {
                    Plugin.RegisterLocalPlayerBuiltTemplate(st);
                }
            }
            catch
            {
            }
        }
    }

    // Per-leader unlock gate: the game keeps ONE shared unlock list for the world.
    // Each client filters it by what THIS player actually built.
    [HarmonyPatch(typeof(NodeTemplate), "IsUnlocked")]
    internal static class Patch_NodeTemplate_IsUnlocked_PerLeader
    {
        private static void Postfix(NodeTemplate __instance, ref bool __result)
        {
            if (!__result || __instance == null || !Plugin.IsPerPlayerProgressionActive())
                return;

            try
            {
                if (Plugin.IsEyeTemplate(__instance))
                    return;

                if (!Plugin.AreRequirementsMetByLocalPlayer(__instance))
                    __result = false;
            }
            catch
            {
            }
        }
    }

    // Per-player journal and blueprint condition gate:
    // Guarantees that the building unlock journal (Diário de Liberação de Construções)
    // evaluates prerequisite structures and village population strictly for the local player.
    [HarmonyPatch(typeof(BlueprintConditionsRule), "CheckRequiredStructures")]
    internal static class Patch_BlueprintConditionsRule_CheckRequiredStructures
    {
        private static void Postfix(BlueprintConditionsRule __instance, ref bool __result)
        {
            if (!__result || __instance == null || !Plugin.IsPerPlayerProgressionActive())
                return;

            try
            {
                Il2CppSystem.Collections.Generic.List<StructureTemplate> reqStructures = __instance.RequiredStructures;
                if (reqStructures != null && reqStructures.Count > 0)
                {
                    for (int j = 0; j < reqStructures.Count; j++)
                    {
                        StructureTemplate req = reqStructures[j];
                        if (req == null || Plugin.IsEyeTemplate(req))
                            continue;

                        if (!Plugin.IsTemplateBuiltByLocalPlayer(req, true))
                        {
                            __result = false;
                            return;
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintConditionsRule), "CheckRequiredPopulation")]
    internal static class Patch_BlueprintConditionsRule_CheckRequiredPopulation
    {
        private static void Postfix(BlueprintConditionsRule __instance, ref bool __result)
        {
            if (!__result || __instance == null || !Plugin.IsPerPlayerProgressionActive())
                return;

            try
            {
                int reqPop = __instance.RequiredPopulation;
                if (reqPop > 0)
                {
                    if (!Plugin.LocalPlayerHasEye || Plugin.LocalPlayerEyeIndex < 0)
                    {
                        __result = false;
                        return;
                    }

                    if (Plugin.GetVillagePopulation(Plugin.LocalPlayerEyeIndex) < reqPop)
                    {
                        __result = false;
                    }
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintConditionsRule), "CheckAll")]
    internal static class Patch_BlueprintConditionsRule_CheckAll
    {
        private static void Postfix(BlueprintConditionsRule __instance, ref bool __result)
        {
            if (!__result || __instance == null || !Plugin.IsPerPlayerProgressionActive())
                return;

            try
            {
                if (!__instance.CheckRequiredStructures() || !__instance.CheckRequiredPopulation())
                {
                    __result = false;
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintConditionsRule), "CheckWithData")]
    internal static class Patch_BlueprintConditionsRule_CheckWithData
    {
        private static void Postfix(BlueprintConditionsRule __instance, ref bool __result)
        {
            if (!__result || __instance == null || !Plugin.IsPerPlayerProgressionActive())
                return;

            try
            {
                if (!__instance.CheckRequiredStructures() || !__instance.CheckRequiredPopulation())
                {
                    __result = false;
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(VillagerOutlet), "SpawnVillager")]
    internal static class Patch_VillagerOutlet_SpawnVillager
    {
        private static void Prefix(VillagerOutlet __instance)
        {
            if (!Plugin.IsModActive() || __instance == null || __instance.transform == null)
                return;

            Vector3 eyePos = __instance.transform.position;
            if (__instance.OwnerStructure != null && __instance.OwnerStructure.transform != null)
            {
                eyePos = __instance.OwnerStructure.transform.position;
            }

            Plugin.PendingSpawnEyePos = eyePos;
            Plugin.HasPendingSpawnEye = true;
        }

        private static void Postfix(VillagerOutlet __instance)
        {
            if (!Plugin.HasPendingSpawnEye)
                return;

            Plugin.HasPendingSpawnEye = false;
            Vector3 eyePos = Plugin.PendingSpawnEyePos;

            try
            {
                MultipleEyeHost.SyncIndependentVillages();
                int eyeIdx = Plugin.GetClosestEyeIndex(eyePos);
                Il2CppArrayBase<Villager> villagers = UnityEngine.Object.FindObjectsOfType<Villager>();
                if (villagers != null)
                {
                    for (int i = 0; i < villagers.Length; i++)
                    {
                        Villager v = villagers[i];
                        if (v == null || v.transform == null)
                            continue;

                        if (Plugin.HorizontalDistSq(v.transform.position, eyePos) <= 625.0f)
                        {
                            bool canModifyState = true;
                            try
                            {
                                if (v.Object != null && !v.Object.HasStateAuthority)
                                    canModifyState = false;
                            }
                            catch { }

                            if (canModifyState)
                            {
                                v.HomeOrigin = eyePos;
                            }

                            int vId = v.PersistentUniqueID != 0 ? v.PersistentUniqueID : v.GetInstanceID();
                            if (eyeIdx >= 0)
                            {
                                Plugin.VillagerEyeAssignment[vId] = eyeIdx;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(PopulationManager), "TryGetNextVillager")]
    internal static class Patch_PopulationManager_TryGetNextVillager
    {
        private static void Postfix(PopulationManager __instance, Villager current, ref Villager nextVillager, ref bool __result)
        {
            if (!__result || nextVillager == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (Plugin.IsVillagerInLocalPlayerVillage(nextVillager))
                return;

            Villager cand = nextVillager;
            int safety = 100;
            while (safety-- > 0 && cand != null)
            {
                Villager next = null;
                if (__instance.TryGetNextVillager(cand, out next) && next != null)
                {
                    if (Plugin.IsVillagerInLocalPlayerVillage(next))
                    {
                        nextVillager = next;
                        __result = true;
                        return;
                    }
                    cand = next;
                }
                else
                {
                    break;
                }
            }

            nextVillager = null;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(PopulationManager), "TryGetPreviousVillager")]
    internal static class Patch_PopulationManager_TryGetPreviousVillager
    {
        private static void Postfix(PopulationManager __instance, Villager current, ref Villager previousVillager, ref bool __result)
        {
            if (!__result || previousVillager == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (Plugin.IsVillagerInLocalPlayerVillage(previousVillager))
                return;

            Villager cand = previousVillager;
            int safety = 100;
            while (safety-- > 0 && cand != null)
            {
                Villager prev = null;
                if (__instance.TryGetPreviousVillager(cand, out prev) && prev != null)
                {
                    if (Plugin.IsVillagerInLocalPlayerVillage(prev))
                    {
                        previousVillager = prev;
                        __result = true;
                        return;
                    }
                    cand = prev;
                }
                else
                {
                    break;
                }
            }

            previousVillager = null;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(VillagerMenu), "_SetupVillager")]
    internal static class Patch_VillagerMenu_SetupVillager
    {
        private static void Prefix(VillagerMenu __instance, ref Villager v)
        {
            if (v == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.IsVillagerInLocalPlayerVillage(v))
            {
                Villager mine = Plugin.GetFirstLocalVillageVillager();
                if (mine != null)
                {
                    v = mine;
                }
            }
        }
    }

    [HarmonyPatch(typeof(VillagersListDisplayer), "_CheckVillagerFiltering")]
    internal static class Patch_VillagersListDisplayer_CheckVillagerFiltering
    {
        private static void Postfix(Villager v, ref bool __result)
        {
            if (!__result || v == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.IsVillagerInLocalPlayerVillage(v))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(VillagersListDisplayer), "_CheckDisplayedVillagerOutpost")]
    internal static class Patch_VillagersListDisplayer_CheckDisplayedVillagerOutpost
    {
        private static void Postfix(Villager v, ref bool __result)
        {
            if (!__result || v == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.IsVillagerInLocalPlayerVillage(v))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(VillagerPooledListController), "_CheckVillagerOutpost")]
    internal static class Patch_VillagerPooledListController_CheckVillagerOutpost
    {
        private static void Postfix(Villager v, ref bool __result)
        {
            if (!__result || v == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.IsVillagerInLocalPlayerVillage(v))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(VillagerListController), "_CheckSearchFilter")]
    internal static class Patch_VillagerListController_CheckSearchFilter
    {
        private static void Postfix(Villager v, ref bool __result)
        {
            if (!__result || v == null || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.IsVillagerInLocalPlayerVillage(v))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(PopulationManager), "CreateVillager", new Type[] { typeof(Vector3), typeof(Quaternion), typeof(int), typeof(bool), typeof(bool) })]
    internal static class Patch_PopulationManager_CreateVillager_1
    {
        private static void Prefix(Vector3 position, ref bool showNotification)
        {
            if (!showNotification || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.LocalPlayerHasEye || Plugin.LocalPlayerEyeIndex < 0)
            {
                showNotification = false;
                return;
            }

            if (position.sqrMagnitude > 1.0f)
            {
                int eye = Plugin.GetClosestEyeIndex(position);
                if (eye >= 0 && eye != Plugin.LocalPlayerEyeIndex)
                {
                    showNotification = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PopulationManager), "CreateVillager", new Type[] { typeof(Vector3), typeof(Quaternion), typeof(float), typeof(Villager.DescriptionData), typeof(bool) })]
    internal static class Patch_PopulationManager_CreateVillager_2
    {
        private static void Prefix(Vector3 position, ref bool showNotification)
        {
            if (!showNotification || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.LocalPlayerHasEye || Plugin.LocalPlayerEyeIndex < 0)
            {
                showNotification = false;
                return;
            }

            if (position.sqrMagnitude > 1.0f)
            {
                int eye = Plugin.GetClosestEyeIndex(position);
                if (eye >= 0 && eye != Plugin.LocalPlayerEyeIndex)
                {
                    showNotification = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PopulationManager), "TryAddVillager", new Type[] { typeof(Vector3), typeof(float), typeof(int), typeof(bool) })]
    internal static class Patch_PopulationManager_TryAddVillager_1
    {
        private static void Prefix(Vector3 spawnLocation, ref bool showNotification)
        {
            if (!showNotification || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.LocalPlayerHasEye || Plugin.LocalPlayerEyeIndex < 0)
            {
                showNotification = false;
                return;
            }

            if (spawnLocation.sqrMagnitude > 1.0f)
            {
                int eye = Plugin.GetClosestEyeIndex(spawnLocation);
                if (eye >= 0 && eye != Plugin.LocalPlayerEyeIndex)
                {
                    showNotification = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(PopulationManager), "TryAddVillager", new Type[] { typeof(Vector3), typeof(float), typeof(Villager.DescriptionData), typeof(bool) })]
    internal static class Patch_PopulationManager_TryAddVillager_2
    {
        private static void Prefix(Vector3 spawnLocation, ref bool showNotification)
        {
            if (!showNotification || !Plugin.IsIndependentVillagesActive())
                return;

            if (!Plugin.LocalPlayerHasEye || Plugin.LocalPlayerEyeIndex < 0)
            {
                showNotification = false;
                return;
            }

            if (spawnLocation.sqrMagnitude > 1.0f)
            {
                int eye = Plugin.GetClosestEyeIndex(spawnLocation);
                if (eye >= 0 && eye != Plugin.LocalPlayerEyeIndex)
                {
                    showNotification = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindClosestStructure")]
    internal static class Patch_Settlement_FindClosestStructure
    {
        private static void Prefix(Vector3 agentPos, ref float maxRange)
        {
            Vector3 origin = agentPos;
            Plugin.RestrictRangeToVillage(ref origin, ref maxRange, false);
        }

        private static void Postfix(Vector3 agentPos, ref Structure __result)
        {
            if (__result != null && !Plugin.IsComponentInSameVillage(agentPos, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindStructuresInRange")]
    internal static class Patch_Settlement_FindStructuresInRange
    {
        private static void Prefix(ref Vector3 agentPos, ref float maxRange, out Vector3 __state)
        {
            __state = agentPos;
            Plugin.RestrictRangeToVillage(ref agentPos, ref maxRange, true);
        }

        private static void Postfix(Vector3 __state, Il2CppSystem.Collections.Generic.List<Structure> result, ref int __result)
        {
            if (result == null || result.Count == 0 || !Plugin.IsIndependentVillagesActive())
                return;

            for (int i = result.Count - 1; i >= 0; i--)
            {
                Structure s = result[i];
                if (s != null && !Plugin.IsComponentInSameVillage(__state, s))
                {
                    result.RemoveAt(i);
                }
            }
            __result = result.Count;
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindDiningOutlet")]
    internal static class Patch_Settlement_FindDiningOutlet
    {
        private static void Prefix(ref Vector3 searchOrigin, ref float maxRange)
        {
            Plugin.RestrictRangeToVillage(ref searchOrigin, ref maxRange, true);
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindClosestReligionOutlet")]
    internal static class Patch_Settlement_FindClosestReligionOutlet
    {
        private static void Prefix(ref Vector3 pos, ref float range)
        {
            Plugin.RestrictRangeToVillage(ref pos, ref range, true);
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindTrashcanToDeposit")]
    internal static class Patch_Settlement_FindTrashcanToDeposit
    {
        private static void Prefix(ref Vector3 agentPos, ref float maxRange, out Vector3 __state)
        {
            __state = agentPos;
            Plugin.RestrictRangeToVillage(ref agentPos, ref maxRange, true);
        }

        private static void Postfix(Vector3 __state, ref Interaction __result)
        {
            if (__result != null && !Plugin.IsComponentInSameVillage(__state, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindStorageToDeposit")]
    internal static class Patch_Settlement_FindStorageToDeposit
    {
        private static void Prefix(ref Vector3 agentPos, ref float maxRange, out Vector3 __state)
        {
            __state = agentPos;
            Plugin.RestrictRangeToVillage(ref agentPos, ref maxRange, true);
        }

        private static void Postfix(Vector3 __state, ref Interaction __result)
        {
            if (__result != null && !Plugin.IsComponentInSameVillage(__state, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Settlement_FindItemStorage_ByRef
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            List<MethodBase> targets = new List<MethodBase>();
            MethodInfo[] methods = typeof(Settlement).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "FindItemStorage")
                    continue;

                ParameterInfo[] ps = m.GetParameters();
                for (int j = 0; j < ps.Length; j++)
                {
                    if (ps[j].Name == "agentPos" && ps[j].ParameterType.IsByRef)
                    {
                        targets.Add(m);
                        break;
                    }
                }
            }
            return targets;
        }

        private static void Prefix(ref Vector3 agentPos, ref float maxRange, out Vector3 __state)
        {
            __state = agentPos;
            Plugin.RestrictRangeToVillage(ref agentPos, ref maxRange, true);
        }

        private static void Postfix(Vector3 __state, ref Interaction __result)
        {
            if (__result != null && !Plugin.IsComponentInSameVillage(__state, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Settlement_FindItemStorage_ByVal
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            List<MethodBase> targets = new List<MethodBase>();
            MethodInfo[] methods = typeof(Settlement).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != "FindItemStorage")
                    continue;

                ParameterInfo[] ps = m.GetParameters();
                for (int j = 0; j < ps.Length; j++)
                {
                    if (ps[j].Name == "agentPos" && !ps[j].ParameterType.IsByRef)
                    {
                        targets.Add(m);
                        break;
                    }
                }
            }
            return targets;
        }

        private static void Prefix(Vector3 agentPos, ref float maxRange)
        {
            Vector3 origin = agentPos;
            Plugin.RestrictRangeToVillage(ref origin, ref maxRange, false);
        }

        private static void Postfix(Vector3 agentPos, ref Interaction __result)
        {
            if (__result != null && !Plugin.IsComponentInSameVillage(agentPos, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(Settlement), "CheckUniqueness")]
    internal static class Patch_Settlement_CheckUniqueness
    {
        private static void Postfix(StructureTemplate template, ref bool __result)
        {
            if (!Plugin.IsModActive() || template == null)
                return;

            if (Plugin.IsEyeTemplate(template))
            {
                __result = !Plugin.LocalPlayerHasEye;
                return;
            }

            if (!__result && Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(StructureUpgradesTabPage), "_CheckUniqueness")]
    internal static class Patch_StructureUpgradesTabPage_CheckUniqueness
    {
        private static void Postfix(StructureTemplate template, Structure activeStructure, ref bool __result)
        {
            if (!Plugin.IsModActive() || template == null)
                return;

            if (Plugin.IsEyeTemplate(template))
            {
                __result = !Plugin.LocalPlayerHasEye;
                return;
            }

            if (!__result && Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Settlement), "FindOverlappingOutpost")]
    internal static class Patch_Settlement_FindOverlappingOutpost
    {
        private static void Postfix(ref IOutpostDataHolder __result)
        {
            if (Plugin.IsModActive() && Plugin.InEyeValidation)
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(StructureBuilder), "Build", new Type[] { typeof(StructureTemplate), typeof(Placement), typeof(Settlement), typeof(Structure), typeof(StructureTemplate.Options) })]
    internal static class Patch_StructureBuilder_Build_5
    {
        private static void Prefix(StructureTemplate template, Placement placement)
        {
            if (template != null && Plugin.IsEyeTemplate(template))
            {
                Plugin.LocalPlayerHasEye = true;
                if (placement != null)
                {
                    Vector3 pos = Plugin.GetPlacementPosition(placement);
                    if (pos != Vector3.zero)
                    {
                        Plugin.LocalPlayerEyePosition = pos;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(StructureBuilder), "Build", new Type[] { typeof(Settlement), typeof(Structure), typeof(StructureTemplate.Options) })]
    internal static class Patch_StructureBuilder_Build_3
    {
        private static void Prefix(StructureBuilder __instance)
        {
            if (__instance != null && __instance.StructureTemplate != null && Plugin.IsEyeTemplate(__instance.StructureTemplate))
            {
                Plugin.LocalPlayerHasEye = true;
                if (__instance.BuildPlacement != null)
                {
                    Vector3 pos = Plugin.GetPlacementPosition(__instance.BuildPlacement);
                    if (pos != Vector3.zero)
                    {
                        Plugin.LocalPlayerEyePosition = pos;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(SettlementManager), "Validate")]
    internal static class Patch_SettlementManager_Validate
    {
        private static void Prefix(StructureTemplate template)
        {
            if (Plugin.IsModActive() && Plugin.IsEyeTemplate(template))
            {
                Plugin.RelaxEyeTemplate(template);
                Plugin.InEyeValidation = true;
            }
        }

        private static void Postfix(StructureTemplate template, Placement placement, ref PlacementValidity validity, ref bool __result)
        {
            Plugin.InEyeValidation = false;
            if (template == null || !Plugin.IsModActive())
                return;

            if (Plugin.IsEyeTemplate(template))
            {
                if (Plugin.LocalPlayerHasEye)
                {
                    validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    if (placement != null)
                        placement.Validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    __result = false;
                    return;
                }

                if (Plugin.IsRelaxableValidity(validity))
                {
                    validity = PlacementValidity.VALID;
                    if (placement != null)
                        placement.Validity = PlacementValidity.VALID;
                    __result = true;
                }
                else if (validity == PlacementValidity.VALID)
                {
                    __result = true;
                }

                if (!__result)
                {
                    Plugin.LogBlockedIfEnabled("SettlementManager.Validate", validity);
                }
                return;
            }

            if (Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateUnlockedForLocalPlayer(template))
                {
                    validity = PlacementValidity.BAD_SETUP;
                    if (placement != null)
                        placement.Validity = PlacementValidity.BAD_SETUP;
                    __result = false;
                    return;
                }

                if (validity == PlacementValidity.UNIQUE_AND_ALREADY_PRESENT && !Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    validity = PlacementValidity.VALID;
                    if (placement != null)
                        placement.Validity = PlacementValidity.VALID;
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(StructureBuilder), "Validate")]
    internal static class Patch_StructureBuilder_Validate
    {
        private static void Prefix(StructureTemplate template)
        {
            if (Plugin.IsModActive() && Plugin.IsEyeTemplate(template))
            {
                Plugin.RelaxEyeTemplate(template);
                Plugin.InEyeValidation = true;
            }
        }

        private static void Postfix(StructureTemplate template, Placement placement, ref PlacementValidity validity, ref bool __result)
        {
            Plugin.InEyeValidation = false;
            if (template == null || !Plugin.IsModActive())
                return;

            if (Plugin.IsEyeTemplate(template))
            {
                if (Plugin.LocalPlayerHasEye)
                {
                    validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    if (placement != null)
                        placement.Validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    __result = false;
                    return;
                }

                if (Plugin.IsRelaxableValidity(validity))
                {
                    validity = PlacementValidity.VALID;
                    if (placement != null)
                        placement.Validity = PlacementValidity.VALID;
                    __result = true;
                }
                else if (validity == PlacementValidity.VALID)
                {
                    __result = true;
                }

                if (!__result)
                {
                    Plugin.LogBlockedIfEnabled("StructureBuilder.Validate", validity);
                }
                return;
            }

            if (Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateUnlockedForLocalPlayer(template))
                {
                    validity = PlacementValidity.BAD_SETUP;
                    if (placement != null)
                        placement.Validity = PlacementValidity.BAD_SETUP;
                    __result = false;
                    return;
                }

                if (validity == PlacementValidity.UNIQUE_AND_ALREADY_PRESENT && !Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    validity = PlacementValidity.VALID;
                    if (placement != null)
                        placement.Validity = PlacementValidity.VALID;
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(StructureBuilder), "ValidateCurrent")]
    internal static class Patch_StructureBuilder_ValidateCurrent
    {
        private static void Prefix(StructureBuilder __instance)
        {
            if (Plugin.IsModActive() && __instance != null && Plugin.IsEyeTemplate(__instance.StructureTemplate))
            {
                Plugin.RelaxEyeTemplate(__instance.StructureTemplate);
                Plugin.InEyeValidation = true;
            }
        }

        private static void Postfix(StructureBuilder __instance, ref PlacementValidity validity, ref bool __result)
        {
            Plugin.InEyeValidation = false;
            if (__instance == null || __instance.StructureTemplate == null || !Plugin.IsModActive())
                return;

            StructureTemplate template = __instance.StructureTemplate;
            if (Plugin.IsEyeTemplate(template))
            {
                if (Plugin.LocalPlayerHasEye)
                {
                    validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    if (__instance.BuildPlacement != null)
                        __instance.BuildPlacement.Validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    __result = false;
                    return;
                }

                if (Plugin.IsRelaxableValidity(validity))
                {
                    validity = PlacementValidity.VALID;
                    if (__instance.BuildPlacement != null)
                        __instance.BuildPlacement.Validity = PlacementValidity.VALID;
                    __result = true;
                }
                else if (validity == PlacementValidity.VALID)
                {
                    __result = true;
                }

                if (!__result)
                {
                    Plugin.LogBlockedIfEnabled("StructureBuilder.ValidateCurrent", validity);
                }
                return;
            }

            if (Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateUnlockedForLocalPlayer(template))
                {
                    validity = PlacementValidity.BAD_SETUP;
                    if (__instance.BuildPlacement != null)
                        __instance.BuildPlacement.Validity = PlacementValidity.BAD_SETUP;
                    __result = false;
                    return;
                }

                if (validity == PlacementValidity.UNIQUE_AND_ALREADY_PRESENT && !Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    validity = PlacementValidity.VALID;
                    if (__instance.BuildPlacement != null)
                        __instance.BuildPlacement.Validity = PlacementValidity.VALID;
                    __result = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(AnchorPointPlacementTool), "_ValidatePlacement")]
    internal static class Patch_AnchorPointPlacementTool_ValidatePlacement
    {
        private static void Prefix(NodeStructurePreviewData previewData)
        {
            if (Plugin.IsModActive() && previewData != null && Plugin.IsEyeTemplate(previewData.structureTemplate))
            {
                Plugin.RelaxEyeTemplate(previewData.structureTemplate);
                Plugin.InEyeValidation = true;
            }
        }

        private static void Postfix(AnchorPointPlacementTool __instance, NodeStructurePreviewData previewData, AnchorPointPlacement placement)
        {
            Plugin.InEyeValidation = false;
            if (previewData == null || previewData.structureTemplate == null || placement == null || !Plugin.IsModActive())
                return;

            StructureTemplate template = previewData.structureTemplate;
            if (Plugin.IsEyeTemplate(template))
            {
                if (Plugin.LocalPlayerHasEye)
                {
                    placement.Validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    __instance._SetupPreviewAppearance(previewData);
                    return;
                }

                if (Plugin.IsRelaxableValidity(placement.Validity))
                {
                    placement.Validity = PlacementValidity.VALID;
                    __instance._SetupPreviewAppearance(previewData);
                }
                else if (placement.Validity != PlacementValidity.VALID)
                {
                    Plugin.LogBlockedIfEnabled("AnchorPointPlacementTool._ValidatePlacement", placement.Validity);
                }
                return;
            }

            if (Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateUnlockedForLocalPlayer(template))
                {
                    placement.Validity = PlacementValidity.BAD_SETUP;
                    __instance._SetupPreviewAppearance(previewData);
                    return;
                }

                if (placement.Validity == PlacementValidity.UNIQUE_AND_ALREADY_PRESENT && !Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    placement.Validity = PlacementValidity.VALID;
                    __instance._SetupPreviewAppearance(previewData);
                }
            }
        }
    }

    [HarmonyPatch(typeof(SkewedAnchorPointPlacementTool), "_ValidatePlacement")]
    internal static class Patch_SkewedAnchorPointPlacementTool_ValidatePlacement
    {
        private static void Prefix(NodeStructurePreviewData previewData)
        {
            if (Plugin.IsModActive() && previewData != null && Plugin.IsEyeTemplate(previewData.structureTemplate))
            {
                Plugin.RelaxEyeTemplate(previewData.structureTemplate);
                Plugin.InEyeValidation = true;
            }
        }

        private static void Postfix(SkewedAnchorPointPlacementTool __instance, NodeStructurePreviewData previewData, AnchorPointPlacement placement)
        {
            Plugin.InEyeValidation = false;
            if (previewData == null || previewData.structureTemplate == null || placement == null || !Plugin.IsModActive())
                return;

            StructureTemplate template = previewData.structureTemplate;
            if (Plugin.IsEyeTemplate(template))
            {
                if (Plugin.LocalPlayerHasEye)
                {
                    placement.Validity = PlacementValidity.UNIQUE_AND_ALREADY_PRESENT;
                    __instance._SetupPreviewAppearance(previewData);
                    return;
                }

                if (Plugin.IsRelaxableValidity(placement.Validity))
                {
                    placement.Validity = PlacementValidity.VALID;
                    __instance._SetupPreviewAppearance(previewData);
                }
                else if (placement.Validity != PlacementValidity.VALID)
                {
                    Plugin.LogBlockedIfEnabled("SkewedAnchorPointPlacementTool._ValidatePlacement", placement.Validity);
                }
                return;
            }

            if (Plugin.IsPerPlayerProgressionActive())
            {
                if (!Plugin.IsTemplateUnlockedForLocalPlayer(template))
                {
                    placement.Validity = PlacementValidity.BAD_SETUP;
                    __instance._SetupPreviewAppearance(previewData);
                    return;
                }

                if (placement.Validity == PlacementValidity.UNIQUE_AND_ALREADY_PRESENT && !Plugin.IsTemplateBuiltByLocalPlayer(template, true))
                {
                    placement.Validity = PlacementValidity.VALID;
                    __instance._SetupPreviewAppearance(previewData);
                }
            }
        }
    }

    [HarmonyPatch(typeof(BuildItemsTabPage), "_SetupNodeTemplate")]
    internal static class Patch_BuildItemsTabPage_SetupNodeTemplate
    {
        private static void Prefix(BuildItemsTabPage __instance, NodeTemplate node)
        {
            if (node != null && Plugin.IsModActive() && Plugin.IsEyeTemplate(node))
            {
                Plugin.RelaxEyeTemplate(node);
            }
        }
    }
}
