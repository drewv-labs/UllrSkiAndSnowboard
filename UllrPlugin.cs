using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UllrSkiAndSnowboard.Assets;
using UllrSkiAndSnowboard.Physics;
using UllrSkiAndSnowboard.Structures;

namespace UllrSkiAndSnowboard
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class UllrPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jimmylab.ullrski";
        public const string PluginName = "Ullr's Peak: Mythic Ski & Snowboard";
        public const string PluginVersion = "1.0.1";

        public const string ToggleRideButtonName = "Ullr_ToggleRide";
        public const string PieceCategoryName = "Ullr's Peak";

        public static ManualLogSource Log { get; private set; }

        // Server-synced gameplay configs
        public static ConfigEntry<float> MaxSpeedMultiplier { get; private set; }
        public static ConfigEntry<float> SlopeAccelerationMultiplier { get; private set; }
        public static ConfigEntry<bool> GrantFrostResistance { get; private set; }
        public static ConfigEntry<bool> AllowNonSnowRiding { get; private set; }
        public static ConfigEntry<string> DefaultNewWorldSeed { get; private set; }

        // Client-side UX configs
        public static ConfigEntry<KeyCode> ToggleRideKey { get; private set; }
        public static ConfigEntry<bool> AutoEngageOnSlope { get; private set; }
        public static ConfigEntry<bool> AutoMarkNearestPeak { get; private set; }
        public static ConfigEntry<bool> ShowRidingHud { get; private set; }

        private Harmony _harmony;
        private ButtonConfig _toggleRideButton;
        private bool _contentRegistered;

        private void Awake()
        {
            Log = Logger;
            BindConfiguration();
            RegisterInputs();

            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginGuid);
            PrefabManager.OnVanillaPrefabsAvailable += RegisterAllContent;

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded — Praise Ullr & Moder!");
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterAllContent;
            _harmony?.UnpatchSelf();
        }

        private void BindConfiguration()
        {
            Config.SaveOnConfigSet = true;

            ConfigurationManagerAttributes adminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };

            MaxSpeedMultiplier = Config.Bind("1 - Physics (Server Synced)", "MaxSpeedMultiplier", 1.0f,
                new ConfigDescription("Multiplier on top downhill speed (~25 m/s / 90 km/h at 1.0).",
                    new AcceptableValueRange<float>(0.5f, 2.5f), adminOnly));

            SlopeAccelerationMultiplier = Config.Bind("1 - Physics (Server Synced)", "SlopeAccelerationMultiplier", 1.0f,
                new ConfigDescription("Multiplier on downhill slope gravity acceleration.",
                    new AcceptableValueRange<float>(0.5f, 2.5f), adminOnly));

            GrantFrostResistance = Config.Bind("1 - Physics (Server Synced)", "GrantFrostResistance", true,
                new ConfigDescription("Grant Ullr's Frost Ward (Frost Resistance) while Mythic Skis or Snowboard are equipped.",
                    null, adminOnly));

            AllowNonSnowRiding = Config.Bind("1 - Physics (Server Synced)", "AllowNonSnowRiding", false,
                new ConfigDescription("If true, allows full-speed skiing/snowboarding on grass/dirt outside Mountain/Snow too.",
                    null, adminOnly));

            DefaultNewWorldSeed = Config.Bind("2 - World Seed (Server)", "DefaultNewWorldSeed", "",
                new ConfigDescription("Optional curated world seed applied when creating a brand new world (overridden by ULLR_WORLD_SEED env var).",
                    null, adminOnly));

            ToggleRideKey = Config.Bind("3 - Controls & HUD (Client)", "ToggleRideKey", KeyCode.X,
                "Hotkey to click into / unclip from your equipped Skis or Snowboard.");

            AutoEngageOnSlope = Config.Bind("3 - Controls & HUD (Client)", "AutoEngageOnSlope", true,
                "Automatically click into riding mode when sprinting onto a snowy slope (>8.5 deg) or Jump Ramp.");

            AutoMarkNearestPeak = Config.Bind("3 - Controls & HUD (Client)", "AutoMarkNearestPeak", true,
                "Automatically place a 'Ullr's Summit' minimap pin on the tallest nearby Mountain peak on first equip.");

            ShowRidingHud = Config.Bind("3 - Controls & HUD (Client)", "ShowRidingHud", true,
                "Display the compact speedometer, hop-charge, and slalom timer widget while actively riding.");
        }

        private void RegisterInputs()
        {
            _toggleRideButton = new ButtonConfig
            {
                Name = ToggleRideButtonName,
                Config = ToggleRideKey,
                ActiveInCustomGUI = false,
                ActiveInGUI = false,
                Hint = "Toggle Ski / Snowboard"
            };
            InputManager.Instance.AddButton(PluginGuid, _toggleRideButton);
        }

        private void RegisterAllContent()
        {
            if (_contentRegistered) return;
            _contentRegistered = true;

            ProceduralAssets.Initialize();

            StatusEffect frostWard = RegisterFrostWardStatusEffect();
            RegisterMythicSkis(frostWard);
            RegisterModerSnowboard(frostWard);

            RegisterJumpRampPiece();
            RegisterSlalomGatePiece();
            RegisterRopeTowPiece();

            Log.LogInfo("Registered Ullr's Skis, Moder's Snowboard, and 3 Mountain Park build pieces.");
        }

        private static StatusEffect RegisterFrostWardStatusEffect()
        {
            SE_Stats ward = ScriptableObject.CreateInstance<SE_Stats>();
            ward.name = "SE_UllrFrostWard";
            ward.m_name = "Ullr's Frost Ward";
            ward.m_tooltip = "Mythic frost runes woven by Ullr and Moder shield your bones from freezing mountain winds.";
            ward.m_startMessageType = MessageHud.MessageType.TopLeft;
            ward.m_startMessage = "";
            ward.m_icon = ProceduralAssets.FrostWardIcon;

            if (GrantFrostResistance.Value)
            {
                ward.m_mods = new List<HitData.DamageModPair>
                {
                    new HitData.DamageModPair
                    {
                        m_type = HitData.DamageType.Frost,
                        m_modifier = HitData.DamageModifier.Resistant
                    }
                };
            }

            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(ward, fixReference: false));
            return ward;
        }

        private static void RegisterMythicSkis(StatusEffect frostWard)
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PlayerSkiController.SkisPrefabName, "BeltStrength");
            StripClonedItemVisuals(prefab);

            // Build dropped world model (twin Norse skis side-by-side)
            GameObject visual = new GameObject("DroppedSkisVisual");
            visual.transform.SetParent(prefab.transform, false);
            BuildDroppedSkiPair(visual.transform);

            BoxCollider col = prefab.GetComponent<BoxCollider>() ?? prefab.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.05f, 0f);
            col.size = new Vector3(0.38f, 0.12f, 1.85f);

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;
            shared.m_maxStackSize = 1;
            shared.m_maxQuality = 4;
            shared.m_useDurability = false;
            shared.m_teleportable = true;
            shared.m_equipStatusEffect = frostWard;

            ItemConfig cfg = new ItemConfig
            {
                Name = "Ullr's Frost-Rune Skis",
                Description = "Twin carved ash skis edged in boreal silver and etched with Ullr's glowing frost runes. Includes Skadi's Runic Poles.\n<color=#44EEFF>Equip in Utility slot • Press [X] on snow to shred!</color>",
                Icon = ProceduralAssets.SkisIcon,
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Weight = 2.0f,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 6, 2),
                    new RequirementConfig("LeatherScraps", 2, 1)
                }
            };

            ItemManager.Instance.AddItem(new CustomItem(prefab, fixReference: false, cfg));
        }

        private static void RegisterModerSnowboard(StatusEffect frostWard)
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PlayerSkiController.SnowboardPrefabName, "BeltStrength");
            StripClonedItemVisuals(prefab);

            GameObject visual = new GameObject("DroppedBoardVisual");
            visual.transform.SetParent(prefab.transform, false);
            BuildDroppedSnowboard(visual.transform);

            BoxCollider col = prefab.GetComponent<BoxCollider>() ?? prefab.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.05f, 0f);
            col.size = new Vector3(0.34f, 0.12f, 1.66f);

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;
            shared.m_maxStackSize = 1;
            shared.m_maxQuality = 4;
            shared.m_useDurability = false;
            shared.m_teleportable = true;
            shared.m_equipStatusEffect = frostWard;

            ItemConfig cfg = new ItemConfig
            {
                Name = "Moder's Drakkar Snowboard",
                Description = "A wide twin-tip drakkar snowboard channeled with Moder's frost-crystal runes for deep carves, lofty bunny-hops, and stylish grabs.\n<color=#44EEFF>Equip in Utility slot • Press [X] on snow to shred!</color>",
                Icon = ProceduralAssets.SnowboardIcon,
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Weight = 2.0f,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 6, 2),
                    new RequirementConfig("LeatherScraps", 2, 1)
                }
            };

            ItemManager.Instance.AddItem(new CustomItem(prefab, fixReference: false, cfg));
        }

        private static void RegisterJumpRampPiece()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab("ullr_jump_ramp", "wood_stair");
            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer < 0) pieceLayer = 10;

            GameObject visualRoot = ResetPieceVisualAndWear(prefab, pieceLayer, health: 400f);

            // Smooth curved snow/wood deck + MeshCollider for silky ski transition
            GameObject deckGo = new GameObject("RampDeck");
            deckGo.layer = pieceLayer;
            deckGo.transform.SetParent(visualRoot.transform, false);
            deckGo.AddComponent<MeshFilter>().sharedMesh = ProceduralAssets.RampDeckMesh;
            MeshRenderer deckMr = deckGo.AddComponent<MeshRenderer>();
            deckMr.sharedMaterial = ProceduralAssets.SnowDeckMat;

            MeshCollider mc = deckGo.AddComponent<MeshCollider>();
            mc.sharedMesh = ProceduralAssets.RampDeckMesh;
            mc.convex = false;

            // Glowing Norse side rails
            GameObject railsGo = new GameObject("RampRuneRails");
            railsGo.layer = pieceLayer;
            railsGo.transform.SetParent(visualRoot.transform, false);
            railsGo.AddComponent<MeshFilter>().sharedMesh = ProceduralAssets.RampRailMesh;
            railsGo.AddComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;

            Piece piece = prefab.GetComponent<Piece>();
            piece.m_groundPiece = false;
            piece.m_groundOnly = false;
            piece.m_allowAltGroundPlacement = true;
            piece.m_clipEverything = true;

            prefab.AddComponent<RunicJumpRamp>();

            PieceConfig cfg = new PieceConfig
            {
                Name = "Ullr's Frost-Rune Kicker Ramp",
                Description = "Smooth curved snow & timber launch kicker infused with frost boost runes. Buildable anywhere on the mountain without a Workbench.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategoryName,
                CraftingStation = CraftingStations.None,
                Icon = ProceduralAssets.RampIcon,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 8, 0, recover: true)
                }
            };

            PieceManager.Instance.AddPiece(new CustomPiece(prefab, fixReference: false, cfg));
        }

        private static void RegisterSlalomGatePiece()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab("ullr_slalom_gate", "wood_pole2");
            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer < 0) pieceLayer = 10;

            GameObject visualRoot = ResetPieceVisualAndWear(prefab, pieceLayer, health: 250f);

            // Left & Right 3.4m Slalom Poles (4.2m wide clear corridor in between!)
            float[] xSides = { -2.1f, 2.1f };
            for (int i = 0; i < xSides.Length; i++)
            {
                GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = $"SlalomPole_{i}";
                pole.layer = pieceLayer;
                pole.transform.SetParent(visualRoot.transform, false);
                pole.transform.localPosition = new Vector3(xSides[i], 1.35f, 0f);
                pole.transform.localScale = new Vector3(0.14f, 2.15f, 0.14f); // reaches -0.8m into slope to +3.5m!
                pole.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.SilverEdgeMat;
                // Replace capsule collider with clean BoxCollider on piece layer
                DestroyImmediate(pole.GetComponent<Collider>());
                BoxCollider bc = pole.AddComponent<BoxCollider>();
                bc.size = new Vector3(1.4f, 2.0f, 1.4f);

                // Glowing crystal finial at top of each pole
                GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(orb.GetComponent<Collider>());
                orb.name = $"GateAccentOrb_{i}";
                orb.transform.SetParent(visualRoot.transform, false);
                orb.transform.localPosition = new Vector3(xSides[i], 3.58f, 0f);
                orb.transform.localScale = Vector3.one * 0.28f;
                orb.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;
            }

            // Overhead glowing Norse race banner (at y = 3.25m so riders fly cleanly underneath!)
            GameObject banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(banner.GetComponent<Collider>());
            banner.name = "GateAccentBanner";
            banner.transform.SetParent(visualRoot.transform, false);
            banner.transform.localPosition = new Vector3(0f, 3.22f, 0f);
            banner.transform.localScale = new Vector3(4.32f, 0.42f, 0.08f);
            banner.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;

            if (!ProceduralAssets.IsHeadless)
            {
                GameObject lightGo = new GameObject("GateLight");
                lightGo.transform.SetParent(visualRoot.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 3.1f, 0f);
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 7.5f;
                l.intensity = 1.15f;
                l.color = new Color(0.28f, 0.85f, 1f);
                l.shadows = LightShadows.None;
            }

            Piece piece = prefab.GetComponent<Piece>();
            piece.m_groundPiece = false;
            piece.m_groundOnly = false;
            piece.m_allowAltGroundPlacement = true;
            piece.m_clipEverything = true;

            prefab.AddComponent<SlalomGate>();

            PieceConfig cfg = new PieceConfig
            {
                Name = "Ullr's Slalom Race Gate",
                Description = "Runic mountain race gate. Press [E] to cycle between Start, Checkpoint, and Finish gates to time downhill races with friends!",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategoryName,
                CraftingStation = CraftingStations.None,
                Icon = ProceduralAssets.GateIcon,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 4, 0, recover: true)
                }
            };

            PieceManager.Instance.AddPiece(new CustomPiece(prefab, fixReference: false, cfg));
        }

        private static void RegisterRopeTowPiece()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab("ullr_rope_tow", "wood_pole2");
            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer < 0) pieceLayer = 10;

            GameObject visualRoot = ResetPieceVisualAndWear(prefab, pieceLayer, health: 500f);

            // Sturdy Nordic A-frame timber legs reaching -0.9m into steep snow slopes
            float[] xLegs = { -0.45f, 0.45f };
            float[] zTilts = { -11f, 11f };
            for (int i = 0; i < 2; i++)
            {
                GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(leg.GetComponent<Collider>());
                leg.name = $"PylonLeg_{i}";
                leg.layer = pieceLayer;
                leg.transform.SetParent(visualRoot.transform, false);
                leg.transform.localPosition = new Vector3(xLegs[i], 0.85f, 0f);
                leg.transform.localRotation = Quaternion.Euler(0f, 0f, zTilts[i]);
                leg.transform.localScale = new Vector3(0.18f, 3.4f, 0.18f);
                leg.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.DarkWoodMat;
            }

            // Crossbeam
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(beam.GetComponent<Collider>());
            beam.name = "Crossbeam";
            beam.layer = pieceLayer;
            beam.transform.SetParent(visualRoot.transform, false);
            beam.transform.localPosition = new Vector3(0f, 2.20f, 0f);
            beam.transform.localScale = new Vector3(1.10f, 0.18f, 0.24f);
            beam.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.DarkWoodMat;

            // Interaction & placement collider on visualRoot
            BoxCollider col = visualRoot.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.15f, 0f);
            col.size = new Vector3(1.25f, 2.5f, 0.65f);

            // Overhead spinning Bullwheel (direct child of prefab root so RunicRopeTow.Awake finds "Bullwheel")
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(wheel.GetComponent<Collider>());
            wheel.name = "Bullwheel";
            wheel.transform.SetParent(prefab.transform, false);
            wheel.transform.localPosition = new Vector3(0f, 2.34f, 0f);
            wheel.transform.localScale = new Vector3(0.95f, 0.06f, 0.95f);
            wheel.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.SilverEdgeMat;

            GameObject spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(spoke.GetComponent<Collider>());
            spoke.name = "RuneCross";
            spoke.transform.SetParent(wheel.transform, false);
            spoke.transform.localScale = new Vector3(0.92f, 1.3f, 0.16f);
            spoke.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;

            GameObject anchor = new GameObject("CableAnchor");
            anchor.transform.SetParent(prefab.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 2.34f, 0f);

            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(orb.GetComponent<Collider>());
            orb.name = "EnergyOrb";
            orb.transform.SetParent(prefab.transform, false);
            orb.transform.localPosition = new Vector3(0f, 2.34f, 0f);
            orb.transform.localScale = Vector3.one * 0.24f;
            orb.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;
            orb.SetActive(false);

            Piece piece = prefab.GetComponent<Piece>();
            piece.m_groundPiece = false;
            piece.m_groundOnly = false;
            piece.m_allowAltGroundPlacement = true;
            piece.m_clipEverything = true;

            prefab.AddComponent<RunicRopeTow>();

            PieceConfig cfg = new PieceConfig
            {
                Name = "Ullr's Runic Rope Tow",
                Description = "Chain two or more pylons up a mountain (up to 85m per span) to create a frost-powered uphill ski lift! Press [E] to ride to the summit.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategoryName,
                CraftingStation = CraftingStations.None,
                Icon = ProceduralAssets.RopeTowIcon,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 10, 0, recover: true),
                    new RequirementConfig("LeatherScraps", 2, 0, recover: true)
                }
            };

            PieceManager.Instance.AddPiece(new CustomPiece(prefab, fixReference: false, cfg));
        }

        private static void StripClonedItemVisuals(GameObject prefab)
        {
            for (int i = prefab.transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(prefab.transform.GetChild(i).gameObject);
            }
            foreach (Collider c in prefab.GetComponents<Collider>())
            {
                DestroyImmediate(c);
            }
            foreach (Renderer r in prefab.GetComponents<Renderer>())
            {
                DestroyImmediate(r);
            }
        }

        private static GameObject ResetPieceVisualAndWear(GameObject prefab, int pieceLayer, float health)
        {
            prefab.layer = pieceLayer;
            for (int i = prefab.transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(prefab.transform.GetChild(i).gameObject);
            }
            foreach (Collider c in prefab.GetComponents<Collider>())
            {
                DestroyImmediate(c);
            }

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.layer = pieceLayer;
            visualRoot.transform.SetParent(prefab.transform, false);

            WearNTear wnt = prefab.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_new = visualRoot;
                wnt.m_worn = visualRoot;
                wnt.m_broken = visualRoot;
                wnt.m_wet = null;
                wnt.m_snow = null;
                wnt.m_snowWorn = null;
                wnt.m_snowBroken = null;
                wnt.m_noRoofWear = false;
                wnt.m_noSupportWear = false;
                wnt.m_snowDamageImmune = true;
                wnt.m_health = health;
            }

            return visualRoot;
        }

        private static void BuildDroppedSkiPair(Transform parent)
        {
            float[] xs = { -0.10f, 0.10f };
            foreach (float x in xs)
            {
                GameObject s = new GameObject("Ski");
                s.transform.SetParent(parent, false);
                s.transform.localPosition = new Vector3(x, 0f, 0f);

                GameObject deck = new GameObject("Deck");
                deck.transform.SetParent(s.transform, false);
                deck.AddComponent<MeshFilter>().sharedMesh = ProceduralAssets.SkiMesh;
                deck.AddComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.DarkWoodMat;

                GameObject rune = new GameObject("Rune");
                rune.transform.SetParent(s.transform, false);
                rune.AddComponent<MeshFilter>().sharedMesh = ProceduralAssets.SkiRuneMesh;
                rune.AddComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;
            }
        }

        private static void BuildDroppedSnowboard(Transform parent)
        {
            GameObject deck = new GameObject("Deck");
            deck.transform.SetParent(parent, false);
            deck.AddComponent<MeshFilter>().sharedMesh = ProceduralAssets.SnowboardMesh;
            deck.AddComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.SilverEdgeMat;

            GameObject rune = new GameObject("Rune");
            rune.transform.SetParent(parent, false);
            rune.AddComponent<MeshFilter>().sharedMesh = ProceduralAssets.SnowboardRuneMesh;
            rune.AddComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;
        }
    }
}
