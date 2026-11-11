using System;
using HarmonyLib;
using UnityEngine;
using UllrSkiAndSnowboard.Physics;
using UllrSkiAndSnowboard.Visuals;

namespace UllrSkiAndSnowboard.Patches
{
    [HarmonyPatch]
    public static class HarmonyPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
        [HarmonyPostfix]
        private static void Player_Awake_Postfix(Player __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<PlayerSkiController>() == null)
            {
                __instance.gameObject.AddComponent<PlayerSkiController>();
            }
            if (__instance.GetComponent<SkiVisualRig>() == null)
            {
                __instance.gameObject.AddComponent<SkiVisualRig>();
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
        [HarmonyPrefix]
        private static bool Character_UpdateWalking_Prefix(Character __instance, float dt)
        {
            if (__instance is Player player)
            {
                PlayerSkiController ski = player.GetComponent<PlayerSkiController>();
                if (ski != null && ski.IsOwnerActiveRider)
                {
                    ski.FixedUpdateRidePhysics(dt);
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundContact))]
        [HarmonyPrefix]
        private static void Character_UpdateGroundContact_Prefix(Character __instance)
        {
            if (__instance is Player player)
            {
                PlayerSkiController ski = player.GetComponent<PlayerSkiController>();
                if (ski != null && ski.IsOwnerActiveRider)
                {
                    // Prevent vanilla vertical fall-damage instakills on steep mountain drops
                    __instance.m_maxAirAltitude = __instance.transform.position.y;
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        [HarmonyPrefix]
        private static bool Character_Jump_Prefix(Character __instance)
        {
            if (__instance is Player player)
            {
                PlayerSkiController ski = player.GetComponent<PlayerSkiController>();
                if (ski != null && ski.IsOwnerActiveRider)
                {
                    // Handled by PlayerSkiController crouch-charged Bunny-Hop & Rope-Tow hop-off
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        [HarmonyPostfix]
        private static void Game_Start_Postfix()
        {
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<string, string, float, int, float>("Ullr_RaceBroadcast", RPC_RaceBroadcast);
            }
        }

        private static void RPC_RaceBroadcast(long sender, string playerName, string gearName, float timeSeconds, int gates, float peakKmh)
        {
            string msg = $"{playerName} shredded the Slalom on {gearName} in {timeSeconds:0.00}s ({gates} gates, peak {peakKmh:0} km/h)!";
            UllrPlugin.Log?.LogInfo($"[Slalom Race] {msg}");

            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, msg);
            }
            if (Chat.instance != null)
            {
                Chat.instance.AddInworldText(null, sender, Vector3.zero, Talker.Type.Shout, UserInfo.GetLocalUser(),
                    $"<color=#44EEFF>[Ullr's Slalom]</color> {msg}");
            }
        }

        [HarmonyPatch(typeof(World), nameof(World.GenerateSeed))]
        [HarmonyPostfix]
        private static void World_GenerateSeed_Postfix(ref string __result)
        {
            string envSeed = Environment.GetEnvironmentVariable("ULLR_WORLD_SEED");
            string cfgSeed = UllrPlugin.DefaultNewWorldSeed != null ? UllrPlugin.DefaultNewWorldSeed.Value : string.Empty;
            string chosen = !string.IsNullOrWhiteSpace(envSeed) ? envSeed.Trim() : cfgSeed?.Trim();

            if (!string.IsNullOrEmpty(chosen))
            {
                __result = chosen;
                UllrPlugin.Log?.LogInfo($"[WorldGen] Applied curated ski mountain world seed: '{__result}'");
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        [HarmonyPostfix]
        private static void ZoneSystem_Start_Postfix()
        {
            if (WorldGenerator.instance == null || WorldGenerator.instance.m_world == null) return;
            World w = WorldGenerator.instance.m_world;
            float maxPeak = 0f;
            Vector2 peakPos = Vector2.zero;

            for (float r = 400f; r <= 3000f; r += 100f)
            {
                for (int i = 0; i < 48; i++)
                {
                    float a = (i / 48f) * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * r;
                    float z = Mathf.Sin(a) * r;
                    if (WorldGenerator.instance.GetBiome(x, z) == Heightmap.Biome.Mountain)
                    {
                        float h = WorldGenerator.instance.GetHeight(x, z);
                        if (h > maxPeak)
                        {
                            maxPeak = h;
                            peakPos = new Vector2(x, z);
                        }
                    }
                }
            }

            UllrPlugin.Log?.LogInfo(
                $"[World Ready] World='{w.m_name}' Seed='{w.m_seedName}' — Nearest Major Ski Summit: {maxPeak:0}m elevation at ({peakPos.x:0}, {peakPos.y:0}) ({peakPos.magnitude:0}m from center)");
        }
    }
}
