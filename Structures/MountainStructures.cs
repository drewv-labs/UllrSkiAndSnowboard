using System.Collections.Generic;
using UnityEngine;
using UllrSkiAndSnowboard.Assets;

namespace UllrSkiAndSnowboard.Structures
{
    public class RunicJumpRamp : MonoBehaviour
    {
        public static readonly HashSet<RunicJumpRamp> ActiveRamps = new HashSet<RunicJumpRamp>();
        private static readonly Dictionary<int, RunicJumpRamp> ColliderLookup = new Dictionary<int, RunicJumpRamp>();

        private Collider[] _colliders;

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
            ActiveRamps.Add(this);
            if (_colliders != null)
            {
                foreach (Collider c in _colliders)
                {
                    if (c != null) ColliderLookup[c.GetInstanceID()] = this;
                }
            }
        }

        private void OnDestroy()
        {
            ActiveRamps.Remove(this);
            if (_colliders != null)
            {
                foreach (Collider c in _colliders)
                {
                    if (c != null) ColliderLookup.Remove(c.GetInstanceID());
                }
            }
        }

        public static bool TryGetRamp(Collider col, out RunicJumpRamp ramp)
        {
            if (col == null)
            {
                ramp = null;
                return false;
            }
            return ColliderLookup.TryGetValue(col.GetInstanceID(), out ramp) && ramp != null;
        }
    }

    public enum SlalomGateMode
    {
        Start = 0,
        Checkpoint = 1,
        Finish = 2
    }

    public class SlalomGate : MonoBehaviour, Hoverable, Interactable
    {
        public static readonly List<SlalomGate> ActiveGates = new List<SlalomGate>();
        private static readonly int GateModeHash = "ullr_gate_mode".GetStableHashCode();

        private ZNetView _nview;
        private MeshRenderer[] _accentRenderers;
        private Light _gateLight;
        private SlalomGateMode _cachedMode = (SlalomGateMode)(-1);

        public SlalomGateMode Mode
        {
            get
            {
                if (_nview != null && _nview.IsValid() && _nview.GetZDO() != null)
                {
                    return (SlalomGateMode)Mathf.Clamp(_nview.GetZDO().GetInt(GateModeHash, 1), 0, 2);
                }
                return SlalomGateMode.Checkpoint;
            }
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            ActiveGates.Add(this);

            var accents = new List<MeshRenderer>();
            foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.gameObject.name.StartsWith("GateAccent"))
                {
                    accents.Add(mr);
                }
            }
            _accentRenderers = accents.ToArray();
            _gateLight = GetComponentInChildren<Light>(true);
            RefreshVisuals(force: true);
        }

        private void OnDestroy()
        {
            ActiveGates.Remove(this);
        }

        private void Update()
        {
            RefreshVisuals(force: false);
        }

        private void RefreshVisuals(bool force)
        {
            SlalomGateMode current = Mode;
            if (!force && current == _cachedMode) return;
            _cachedMode = current;

            if (ProceduralAssets.IsHeadless) return;

            Color baseCol;
            Color emitCol;
            switch (current)
            {
                case SlalomGateMode.Start:
                    baseCol = new Color(0.18f, 1.0f, 0.78f);
                    emitCol = new Color(0.15f, 0.95f, 0.72f) * 2.6f;
                    break;
                case SlalomGateMode.Finish:
                    baseCol = new Color(1.0f, 0.62f, 0.18f);
                    emitCol = new Color(1.0f, 0.48f, 0.12f) * 2.8f;
                    break;
                default:
                    baseCol = new Color(0.28f, 0.80f, 1.0f);
                    emitCol = new Color(0.22f, 0.75f, 1.0f) * 2.4f;
                    break;
            }

            Material mat = ProceduralAssets.CreateGateBannerMaterial(baseCol, emitCol);
            if (_accentRenderers != null)
            {
                foreach (MeshRenderer mr in _accentRenderers)
                {
                    if (mr != null) mr.sharedMaterial = mat;
                }
            }
            if (_gateLight != null)
            {
                _gateLight.color = baseCol;
            }
        }

        public string GetHoverText()
        {
            string modeLabel = Mode switch
            {
                SlalomGateMode.Start => "<color=#33FFCC>START GATE</color>",
                SlalomGateMode.Finish => "<color=#FFAA33>FINISH GATE</color>",
                _ => "<color=#55CCFF>CHECKPOINT GATE</color>"
            };
            return Localization.instance.Localize(
                $"Ullr's Slalom Gate ({modeLabel})\n[<color=yellow><b>$KEY_Use</b></color>] Cycle Gate Type");
        }

        public string GetHoverName() => "Ullr's Slalom Gate";

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _nview == null || !_nview.IsValid()) return false;
            _nview.ClaimOwnership();
            int next = ((int)Mode + 1) % 3;
            _nview.GetZDO().Set(GateModeHash, next);
            RefreshVisuals(force: true);

            string nextName = ((SlalomGateMode)next) switch
            {
                SlalomGateMode.Start => "START Gate",
                SlalomGateMode.Finish => "FINISH Gate",
                _ => "CHECKPOINT Gate"
            };
            user.Message(MessageHud.MessageType.Center, $"Slalom Gate set to {nextName}");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    public class RunicRopeTow : MonoBehaviour, Hoverable, Interactable
    {
        public static readonly List<RunicRopeTow> ActivePylons = new List<RunicRopeTow>();
        public const float MaxSpanMeters = 85f;

        private Transform _bullwheel;
        private Transform _cableAnchor;
        private LineRenderer _cableLine;
        private Transform _energyOrb;
        private RunicRopeTow _uphillTarget;
        private float _relinkTimer;
        private float _orbProgress;

        public RunicRopeTow UphillTarget => _uphillTarget;
        public Vector3 AnchorWorldPos => _cableAnchor != null ? _cableAnchor.position : transform.position + Vector3.up * 2.1f;

        private void Awake()
        {
            ActivePylons.Add(this);
            _bullwheel = transform.Find("Bullwheel");
            _cableAnchor = transform.Find("CableAnchor");
            _energyOrb = transform.Find("EnergyOrb");

            if (!ProceduralAssets.IsHeadless)
            {
                GameObject lineObj = new GameObject("RunicTowCable");
                lineObj.transform.SetParent(transform, false);
                _cableLine = lineObj.AddComponent<LineRenderer>();
                _cableLine.useWorldSpace = true;
                _cableLine.positionCount = 2;
                _cableLine.startWidth = 0.085f;
                _cableLine.endWidth = 0.085f;
                _cableLine.sharedMaterial = ProceduralAssets.FrostRuneMat;
                _cableLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cableLine.enabled = false;
            }
            _relinkTimer = 0.1f;
        }

        private void OnDestroy()
        {
            ActivePylons.Remove(this);
        }

        private void Update()
        {
            _relinkTimer -= Time.deltaTime;
            if (_relinkTimer <= 0f)
            {
                _relinkTimer = 1.0f;
                RefreshUphillLink();
            }

            if (ProceduralAssets.IsHeadless) return;

            if (_uphillTarget != null)
            {
                Vector3 a = AnchorWorldPos;
                Vector3 b = _uphillTarget.AnchorWorldPos;
                if (_cableLine != null)
                {
                    _cableLine.enabled = true;
                    _cableLine.SetPosition(0, a);
                    _cableLine.SetPosition(1, b);
                }
                if (_bullwheel != null)
                {
                    _bullwheel.Rotate(Vector3.up, 160f * Time.deltaTime, Space.Self);
                }
                if (_energyOrb != null)
                {
                    _energyOrb.gameObject.SetActive(true);
                    _orbProgress = (_orbProgress + Time.deltaTime * 0.45f) % 1f;
                    _energyOrb.position = Vector3.Lerp(a, b, _orbProgress);
                }
            }
            else
            {
                if (_cableLine != null) _cableLine.enabled = false;
                if (_energyOrb != null) _energyOrb.gameObject.SetActive(false);
            }
        }

        public void RefreshUphillLink()
        {
            RunicRopeTow best = null;
            float bestDist = MaxSpanMeters;
            Vector3 myPos = transform.position;

            for (int i = 0; i < ActivePylons.Count; i++)
            {
                RunicRopeTow other = ActivePylons[i];
                if (other == null || other == this) continue;
                float dy = other.transform.position.y - myPos.y;
                if (dy < 1.0f) continue; // Must climb uphill
                float d = Vector3.Distance(myPos, other.transform.position);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = other;
                }
            }
            _uphillTarget = best;
        }

        public static bool TryFindNearestTowSpan(Vector3 playerPos, float maxCaptureRadius,
            out RunicRopeTow startPylon, out float tAlongSpan, out Vector3 closestPoint)
        {
            startPylon = null;
            tAlongSpan = 0f;
            closestPoint = Vector3.zero;
            float bestDist = maxCaptureRadius;

            for (int i = 0; i < ActivePylons.Count; i++)
            {
                RunicRopeTow p = ActivePylons[i];
                if (p == null || p._uphillTarget == null) continue;

                // Ground-level tow path runs 1.8m below the overhead cable anchors
                Vector3 a = p.transform.position + Vector3.up * 0.35f;
                Vector3 b = p._uphillTarget.transform.position + Vector3.up * 0.35f;
                Vector3 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 1f) continue;

                float t = Mathf.Clamp01(Vector3.Dot(playerPos - a, ab) / lenSq);
                if (t > 0.98f) continue;
                Vector3 pt = a + ab * t;
                float dist = Vector3.Distance(playerPos, pt);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    startPylon = p;
                    tAlongSpan = t;
                    closestPoint = pt;
                }
            }
            return startPylon != null;
        }

        public string GetHoverText()
        {
            string status = _uphillTarget != null
                ? $"<color=#44EEFF>Active Uphill Span ({Vector3.Distance(transform.position, _uphillTarget.transform.position):0}m)</color>\n[<color=yellow><b>$KEY_Use</b></color>] Latch onto Runic Tow"
                : "<color=#FFCC66>Top Summit Pylon</color> (Place another pylon uphill within 85m to extend)";
            return Localization.instance.Localize($"Ullr's Runic Rope Tow\n{status}");
        }

        public string GetHoverName() => "Ullr's Runic Rope Tow";

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user is Player player)
            {
                var ctrl = player.GetComponent<Physics.PlayerSkiController>();
                if (ctrl != null && ctrl.TryLatchOntoTowFromPylon(this))
                {
                    return true;
                }
            }
            if (_uphillTarget == null)
            {
                user.Message(MessageHud.MessageType.Center, "Summit station! Place another Rope Tow higher uphill (up to 85m) to extend.");
            }
            else
            {
                user.Message(MessageHud.MessageType.Center, "Equip Ullr's Skis or Moder's Snowboard first to ride the Runic Tow!");
            }
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    public static class SlalomRaceTracker
    {
        private static bool _raceActive;
        private static float _raceStartTime;
        private static float _peakSpeedMps;
        private static readonly HashSet<int> _clearedGateIds = new HashSet<int>();
        private static readonly Dictionary<int, float> _gateCooldowns = new Dictionary<int, float>();

        public static bool IsRaceActive => _raceActive;
        public static float CurrentElapsed => _raceActive ? Time.time - _raceStartTime : 0f;
        public static int CheckpointsCleared => _clearedGateIds.Count;
        public static float PeakSpeedKmh => _peakSpeedMps * 3.6f;

        public static void UpdateForLocalRider(Player player, Vector3 velocity, string gearDisplayName)
        {
            if (player == null) return;
            float speed = velocity.magnitude;
            if (_raceActive && speed > _peakSpeedMps)
            {
                _peakSpeedMps = speed;
            }

            Vector3 pos = player.transform.position + Vector3.up * 0.9f;
            float now = Time.time;

            for (int i = 0; i < SlalomGate.ActiveGates.Count; i++)
            {
                SlalomGate gate = SlalomGate.ActiveGates[i];
                if (gate == null) continue;

                int id = gate.GetInstanceID();
                if (_gateCooldowns.TryGetValue(id, out float until) && now < until) continue;

                Vector3 local = gate.transform.InverseTransformPoint(pos);
                if (Mathf.Abs(local.x) <= 2.25f && local.y >= -0.8f && local.y <= 3.9f && Mathf.Abs(local.z) <= 1.25f)
                {
                    _gateCooldowns[id] = now + 2.0f;
                    OnPassGate(player, gate, id, speed, gearDisplayName);
                }
            }
        }

        public static void CancelRace()
        {
            _raceActive = false;
            _clearedGateIds.Clear();
        }

        private static void OnPassGate(Player player, SlalomGate gate, int gateId, float speedMps, string gearName)
        {
            PlayGateChime(gate.transform.position, gate.Mode == SlalomGateMode.Finish ? 1.32f : 1.08f);

            switch (gate.Mode)
            {
                case SlalomGateMode.Start:
                    _raceActive = true;
                    _raceStartTime = Time.time;
                    _peakSpeedMps = speedMps;
                    _clearedGateIds.Clear();
                    player.Message(MessageHud.MessageType.Center, "SLALOM RACE STARTED! Shred through the gates!");
                    break;

                case SlalomGateMode.Checkpoint:
                    if (!_raceActive)
                    {
                        player.Message(MessageHud.MessageType.TopLeft, $"Slalom Gate! ({speedMps * 3.6f:0} km/h)");
                        return;
                    }
                    if (_clearedGateIds.Add(gateId))
                    {
                        float split = Time.time - _raceStartTime;
                        player.Message(MessageHud.MessageType.TopLeft,
                            $"Gate {_clearedGateIds.Count} — {split:0.00}s ({speedMps * 3.6f:0} km/h)");
                    }
                    break;

                case SlalomGateMode.Finish:
                    if (!_raceActive) return;
                    float finalTime = Mathf.Max(0.05f, Time.time - _raceStartTime);
                    int gates = _clearedGateIds.Count;
                    float topKmh = Mathf.Max(_peakSpeedMps, speedMps) * 3.6f;
                    _raceActive = false;
                    _clearedGateIds.Clear();

                    player.Message(MessageHud.MessageType.Center,
                        $"FINISH! {finalTime:0.00}s ({gates} gates | Peak {topKmh:0} km/h)");

                    if (ZRoutedRpc.instance != null)
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "Ullr_RaceBroadcast",
                            player.GetPlayerName(), gearName, finalTime, gates, topKmh);
                    }
                    break;
            }
        }

        private static void PlayGateChime(Vector3 pos, float pitch)
        {
            if (ProceduralAssets.IsHeadless || ProceduralAssets.FrostChimeClip == null) return;
            AudioSource.PlayClipAtPoint(ProceduralAssets.FrostChimeClip, pos, 0.85f);
        }
    }
}
