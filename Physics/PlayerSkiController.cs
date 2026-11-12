using System;
using UnityEngine;
using UllrSkiAndSnowboard.Assets;
using UllrSkiAndSnowboard.Structures;

namespace UllrSkiAndSnowboard.Physics
{
    public enum RideGearType
    {
        None = 0,
        Skis = 1,
        Snowboard = 2
    }

    [Flags]
    public enum RideSyncFlags
    {
        None = 0,
        Airborne = 1 << 0,
        Tucking = 1 << 1,
        Braking = 1 << 2,
        Grabbing = 1 << 3,
        WipedOut = 1 << 4,
        Poling = 1 << 5,
        OnRopeTow = 1 << 6
    }

    public class PlayerSkiController : MonoBehaviour
    {
        public const string SkisPrefabName = "UllrSkis";
        public const string SnowboardPrefabName = "ModerSnowboard";

        private static readonly int ZdoRideMode = "ullr_mode".GetStableHashCode();
        private static readonly int ZdoLean = "ullr_lean".GetStableHashCode();
        private static readonly int ZdoSpin = "ullr_spin".GetStableHashCode();
        private static readonly int ZdoFlags = "ullr_flags".GetStableHashCode();
        private static readonly int ZdoSpeed = "ullr_speed".GetStableHashCode();

        private static readonly int AnimForwardSpeed = ZSyncAnimation.GetHash("forward_speed");
        private static readonly int AnimSidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");
        private static readonly int AnimTurnSpeed = ZSyncAnimation.GetHash("turn_speed");
        private static readonly int AnimOnGround = ZSyncAnimation.GetHash("onGround");
        private static readonly int AnimInWater = ZSyncAnimation.GetHash("inWater");
        private static readonly int AnimEncumbered = ZSyncAnimation.GetHash("encumbered");
        private static readonly int AnimFalling = ZSyncAnimation.GetHash("falling");

        private Player _player;
        private ZNetView _nview;

        // Owner simulation state
        private RideGearType _equippedGear = RideGearType.None;
        private bool _ridingActive;
        private float _headingYaw;
        private Vector3 _smoothedNormal = Vector3.up;
        private Vector3 _prevPlanarVel;
        private float _leanAngle;
        private float _trickSpinYaw;
        private float _accumulatedSpin;
        private float _airTime;
        private bool _wasAirborne;
        private float _hopCharge;
        private bool _chargingHop;
        private float _poleCooldown;
        private float _poleAnimTimer;
        private float _wipeoutTimer;
        private float _rampBoostCooldown;
        private float _autoEngageTimer;
        private float _nonSnowGraceTimer;
        private bool _peakMarkedThisSession;

        // Rope tow state
        private RunicRopeTow _activeTowStart;
        private float _towProgress;
        private float _towDetachCooldown;

        // Synced / readable state for visuals (owner + remote multiplayer clients)
        public RideGearType EquippedGear => _equippedGear;
        public RideGearType SyncedRideMode { get; private set; }
        public float SyncedLeanAngle { get; private set; }
        public float SyncedSpinYaw { get; private set; }
        public RideSyncFlags SyncedFlags { get; private set; }
        public float SyncedSpeed { get; private set; }
        public float HopChargeRatio => Mathf.Clamp01(_hopCharge / 0.45f);
        public float CurrentAirSpinDegrees => Mathf.Abs(_accumulatedSpin);

        public bool IsOwnerActiveRider => _ridingActive && _equippedGear != RideGearType.None && IsOwner();

        private float _zdoSyncTimer;
        private GUIStyle _hudBoxStyle;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudSubStyle;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _nview = GetComponent<ZNetView>();
        }

        private bool IsOwner()
        {
            return _nview != null && _nview.IsValid() && _nview.IsOwner();
        }

        private void Update()
        {
            if (_player == null || _nview == null || !_nview.IsValid()) return;

            if (IsOwner() && _player == Player.m_localPlayer)
            {
                UpdateLocalOwnerInput(Time.deltaTime);
                SyncOwnerToZdo(Time.deltaTime, force: false);
            }
            else
            {
                ReadRemoteFromZdo(Time.deltaTime);
            }
        }

        private void UpdateLocalOwnerInput(float dt)
        {
            RideGearType prevGear = _equippedGear;
            _equippedGear = DetectEquippedGearFromPlayer();

            if (_equippedGear != prevGear)
            {
                if (_equippedGear == RideGearType.None)
                {
                    SetRidingActive(false, silent: true);
                }
                else
                {
                    OnGearNewlyEquipped(_equippedGear);
                }
            }

            if (_towDetachCooldown > 0f) _towDetachCooldown -= dt;
            if (_poleCooldown > 0f) _poleCooldown -= dt;
            if (_poleAnimTimer > 0f) _poleAnimTimer -= dt;
            if (_rampBoostCooldown > 0f) _rampBoostCooldown -= dt;

            if (_equippedGear == RideGearType.None) return;

            // Safety dismount conditions (building with Hammer, swimming, dead, sitting/ship)
            if (_player.IsDead() || _player.IsSwimming() || _player.InPlaceMode() || _player.IsAttached() || _player.IsTeleporting())
            {
                if (_ridingActive)
                {
                    SetRidingActive(false, silent: true);
                }
                return;
            }

            bool takeInput = _player.TakeInput();

            // Manual toggle hotkey (default X)
            if (takeInput && (ZInput.GetButtonDown(UllrPlugin.ToggleRideButtonName) || Input.GetKeyDown(UllrPlugin.ToggleRideKey.Value)))
            {
                if (_ridingActive)
                {
                    SetRidingActive(false, silent: false);
                }
                else if (CanStartRidingHere(out string blockReason))
                {
                    SetRidingActive(true, silent: false);
                }
                else
                {
                    _player.Message(MessageHud.MessageType.Center, blockReason);
                }
            }

            // Auto-engage when sprinting onto a snowy downhill slope or Jump Ramp
            if (!_ridingActive && UllrPlugin.AutoEngageOnSlope.Value && IsOnSnowOrRamp(out float slopeDeg) && slopeDeg >= 8.5f && _player.IsRunning())
            {
                _autoEngageTimer += dt;
                if (_autoEngageTimer >= 0.25f)
                {
                    _autoEngageTimer = 0f;
                    SetRidingActive(true, silent: false);
                }
            }
            else
            {
                _autoEngageTimer = 0f;
            }

            if (!_ridingActive) return;

            // Rope tow quick-latch if riding close to a tow line and pressing Use (E)
            if (_activeTowStart == null && _towDetachCooldown <= 0f && takeInput && ZInput.GetButtonDown("Use"))
            {
                if (RunicRopeTow.TryFindNearestTowSpan(_player.transform.position, 3.2f, out RunicRopeTow spanStart, out float tSpan, out _))
                {
                    _activeTowStart = spanStart;
                    _towProgress = tSpan;
                    _player.Message(MessageHud.MessageType.Center, "Latched onto Ullr's Runic Tow! ([Space] to hop off)");
                }
            }

            // Bunny-hop charge & release (Space / Jump)
            if (_wipeoutTimer <= 0f && takeInput)
            {
                if (_activeTowStart != null)
                {
                    if (ZInput.GetButtonDown("Jump") || ZInput.GetButtonDown("JoyJump"))
                    {
                        DetachFromTow(jumpPop: true);
                    }
                }
                else
                {
                    bool jumpHeld = ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump");
                    if (jumpHeld && IsGroundedForSki())
                    {
                        _chargingHop = true;
                        _hopCharge = Mathf.Min(0.45f, _hopCharge + dt);
                    }
                    else if (_chargingHop && !jumpHeld)
                    {
                        _chargingHop = false;
                        if (IsGroundedForSki())
                        {
                            PerformBunnyHop();
                        }
                        _hopCharge = 0f;
                    }
                }
            }
            else
            {
                _chargingHop = false;
                _hopCharge = 0f;
            }

            // Update Slalom Race gates
            string gearTitle = _equippedGear == RideGearType.Snowboard ? "Moder's Snowboard" : "Ullr's Skis";
            SlalomRaceTracker.UpdateForLocalRider(_player, _player.m_body != null ? _player.m_body.linearVelocity : Vector3.zero, gearTitle);
        }

        public bool TryLatchOntoTowFromPylon(RunicRopeTow pylon)
        {
            if (pylon == null || pylon.UphillTarget == null) return false;
            if (_equippedGear == RideGearType.None) return false;

            if (!_ridingActive)
            {
                SetRidingActive(true, silent: true);
            }
            _activeTowStart = pylon;
            _towProgress = 0.02f;
            _wipeoutTimer = 0f;
            _player.Message(MessageHud.MessageType.Center, "Riding Ullr's Runic Tow uphill! ([Space] to hop off)");
            return true;
        }

        private void DetachFromTow(bool jumpPop)
        {
            _activeTowStart = null;
            _towDetachCooldown = 0.8f;
            if (jumpPop && _player.m_body != null)
            {
                Vector3 v = _player.m_body.linearVelocity;
                v.y = Mathf.Max(v.y, 4.2f);
                _player.m_body.linearVelocity = v;
                PlayClip(ProceduralAssets.JumpPopClip, 0.7f, 1.05f);
            }
        }

        public void SetRidingActive(bool active, bool silent)
        {
            if (_ridingActive == active) return;
            _ridingActive = active;
            _chargingHop = false;
            _hopCharge = 0f;
            _trickSpinYaw = 0f;
            _accumulatedSpin = 0f;
            _wipeoutTimer = 0f;
            _activeTowStart = null;

            if (active)
            {
                Vector3 fwd = _player.m_body != null && _player.m_body.linearVelocity.sqrMagnitude > 0.5f
                    ? Vector3.ProjectOnPlane(_player.m_body.linearVelocity, Vector3.up)
                    : Vector3.ProjectOnPlane(_player.GetLookDir(), Vector3.up);
                if (fwd.sqrMagnitude < 0.01f) fwd = _player.transform.forward;
                _headingYaw = Quaternion.LookRotation(fwd.normalized, Vector3.up).eulerAngles.y;
                _smoothedNormal = Vector3.up;
                _nonSnowGraceTimer = 2.5f;
                _prevPlanarVel = _player.m_body != null
                    ? Vector3.ProjectOnPlane(_player.m_body.linearVelocity, Vector3.up)
                    : Vector3.zero;

                if (!silent)
                {
                    string modeText = _equippedGear == RideGearType.Snowboard
                        ? "Clicked into Moder's Drakkar Snowboard!"
                        : "Clicked into Ullr's Frost-Rune Skis!";
                    _player.Message(MessageHud.MessageType.Center, modeText);
                    PlayClip(ProceduralAssets.FrostChimeClip, 0.65f, 1.15f);
                }
            }
            else
            {
                SyncedRideMode = RideGearType.None;
                SyncedLeanAngle = 0f;
                SyncedSpinYaw = 0f;
                SyncedFlags = RideSyncFlags.None;
                if (!silent)
                {
                    _player.Message(MessageHud.MessageType.TopLeft, "Skis unclipped — walking mode");
                }
            }

            SyncOwnerToZdo(0f, force: true);
        }

        /// <summary>
        /// Called from Harmony prefix on Character.UpdateWalking(float dt) when IsOwnerActiveRider is true.
        /// </summary>
        public void FixedUpdateRidePhysics(float dt)
        {
            if (_player == null || _player.m_body == null) return;

            Rigidbody body = _player.m_body;
            body.useGravity = true;

            // Keep fall-damage tracker continuously reset while actively riding; landings are evaluated in EvaluateAirLanding!
            _player.m_maxAirAltitude = _player.transform.position.y;

            if (_activeTowStart != null)
            {
                UpdateRopeTowMotion(body, dt);
                return;
            }

            bool grounded = SampleGround(out Vector3 groundNormal, out bool onSnow, out RunicJumpRamp onRamp);
            _smoothedNormal = Vector3.Slerp(_smoothedNormal, grounded ? groundNormal : Vector3.up, dt * 10f).normalized;

            // Handle wipeout tumble recovery (scaled to 40% speed regime)
            if (_wipeoutTimer > 0f)
            {
                _wipeoutTimer -= dt;
                Vector3 v = body.linearVelocity;
                Vector3 horiz = new Vector3(v.x, 0f, v.z);
                horiz = Vector3.MoveTowards(horiz, Vector3.zero, 5.6f * dt);
                body.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);
                _prevPlanarVel = horiz;
                _leanAngle = Mathf.Lerp(_leanAngle, 55f, dt * 8f);
                _trickSpinYaw += 380f * dt;
                if (_wipeoutTimer <= 0f)
                {
                    _trickSpinYaw = 0f;
                    _leanAngle = 0f;
                }
                ApplyPlayerVisualAndAnim(body, grounded, 0f, RideSyncFlags.WipedOut);
                return;
            }

            // Detect high-speed head-on obstacle collision (trees, boulders, cliffs) — scaled to 40% speed curve
            Vector3 currentPlanar = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            bool brakingInput = _player.TakeInput() && GetMoveForwardInput() < -0.25f;
            if (_prevPlanarVel.magnitude > 5.8f && !brakingInput)
            {
                float speedLoss = _prevPlanarVel.magnitude - currentPlanar.magnitude;
                if (speedLoss > 4.0f)
                {
                    TriggerWipeout("Crashed! Watch those trees & rocks!");
                    return;
                }
            }

            float steerInput = _player.TakeInput() ? GetSteerInput() : 0f;
            float fwdInput = _player.TakeInput() ? GetMoveForwardInput() : 0f;

            bool isBoard = _equippedGear == RideGearType.Snowboard;
            // 40% of original 24.5 / 26.0 m/s -> 9.8 m/s (~35 km/h) Snowboard, 10.4 m/s (~37 km/h) Skis
            float maxSpeed = (isBoard ? 9.8f : 10.4f) * UllrPlugin.MaxSpeedMultiplier.Value;

            RideSyncFlags flags = RideSyncFlags.None;

            if (grounded)
            {
                // Landing check if coming out of big air
                if (_wasAirborne && _airTime > 0.25f)
                {
                    EvaluateAirLanding(body, groundNormal);
                }
                _wasAirborne = false;
                _airTime = 0f;
                _accumulatedSpin = 0f;
                _trickSpinYaw = Mathf.Lerp(_trickSpinYaw, 0f, dt * 12f);

                // Check foothill non-snow runout
                if (onSnow || onRamp != null || UllrPlugin.AllowNonSnowRiding.Value)
                {
                    _nonSnowGraceTimer = 2.5f;
                }
                else
                {
                    _nonSnowGraceTimer -= dt;
                    if (currentPlanar.magnitude < 0.65f && _nonSnowGraceTimer <= 0f)
                    {
                        SetRidingActive(false, silent: false);
                        return;
                    }
                }

                // Steer yaw + gentle camera alignment when pressing W
                float turnRate = isBoard ? 128f : 112f;
                float speedFactor = Mathf.Lerp(1.15f, 0.72f, Mathf.Clamp01(currentPlanar.magnitude / maxSpeed));
                float yawDelta = steerInput * turnRate * speedFactor * dt;

                if (fwdInput > 0.1f && Mathf.Abs(steerInput) < 0.2f)
                {
                    Vector3 lookFlat = Vector3.ProjectOnPlane(_player.GetLookDir(), Vector3.up);
                    if (lookFlat.sqrMagnitude > 0.01f)
                    {
                        float targetYaw = Quaternion.LookRotation(lookFlat.normalized, Vector3.up).eulerAngles.y;
                        yawDelta += Mathf.DeltaAngle(_headingYaw, targetYaw) * 1.8f * dt;
                    }
                }
                _headingYaw = Mathf.Repeat(_headingYaw + yawDelta, 360f);

                Quaternion headingRot = Quaternion.Euler(0f, _headingYaw, 0f);
                Vector3 flatFwd = headingRot * Vector3.forward;
                Vector3 slopeFwd = Vector3.ProjectOnPlane(flatFwd, groundNormal).normalized;
                if (slopeFwd.sqrMagnitude < 0.01f) slopeFwd = flatFwd;
                Vector3 slopeRight = Vector3.Cross(groundNormal, slopeFwd).normalized;

                Vector3 vel = body.linearVelocity;
                // Project onto ground tangent plane while keeping slight downward stickiness so bumps don't chatter
                Vector3 slopeVel = Vector3.ProjectOnPlane(vel, groundNormal);

                // Slope gravity pull — 40% of original 1.32f curve -> 0.528f
                float gravMult = 0.528f * UllrPlugin.SlopeAccelerationMultiplier.Value;
                if (fwdInput > 0.2f && slopeVel.magnitude > 2.4f)
                {
                    gravMult *= 1.15f; // Aero tuck boost
                    flags |= RideSyncFlags.Tucking;
                }
                Vector3 slopeGravity = Vector3.ProjectOnPlane(UnityEngine.Physics.gravity * gravMult, groundNormal);
                slopeVel += slopeGravity * dt;

                // Jump Ramp boost & lip launch! (scaled to 40% horizontal speed curve)
                if (onRamp != null)
                {
                    Vector3 rampFwd = Vector3.ProjectOnPlane(onRamp.transform.forward, groundNormal).normalized;
                    if (Vector3.Dot(slopeFwd, rampFwd) > 0.25f)
                    {
                        slopeVel += rampFwd * (2.6f * dt);
                        Vector3 localOnRamp = onRamp.transform.InverseTransformPoint(_player.transform.position);
                        if (localOnRamp.z > 2.05f && _rampBoostCooldown <= 0f && slopeVel.magnitude > 2.0f)
                        {
                            _rampBoostCooldown = 0.65f;
                            Vector3 launchVel = slopeVel + Vector3.up * 3.8f + onRamp.transform.forward * 1.12f;
                            body.linearVelocity = launchVel;
                            _prevPlanarVel = new Vector3(launchVel.x, 0f, launchVel.z);
                            _wasAirborne = true;
                            PlayClip(ProceduralAssets.JumpPopClip, 0.85f, 1.12f);
                            ApplyPlayerVisualAndAnim(body, false, steerInput, RideSyncFlags.Airborne);
                            return;
                        }
                    }
                }

                // Decompose into forward carve & lateral edge slip
                float vFwd = Vector3.Dot(slopeVel, slopeFwd);
                float vLat = Vector3.Dot(slopeVel, slopeRight);

                float edgeGrip = isBoard ? 7.8f : 8.6f;
                if (!onSnow && onRamp == null && !UllrPlugin.AllowNonSnowRiding.Value)
                {
                    edgeGrip = 5.0f;
                }
                float latDamp = Mathf.Min(1f, edgeGrip * dt);
                float carvedLat = Mathf.Abs(vLat) * latDamp;
                vLat *= (1f - latDamp);

                // Transfer 68% of carved lateral momentum into forward glide when moving forward!
                if (vFwd >= 0f)
                {
                    vFwd += carvedLat * 0.68f;
                }

                // Pole-push / skate-push on flat or slight uphill when pressing W (40% of original 9.2 / 3.5 / 4.1)
                if (fwdInput > 0.25f && vFwd < 3.68f && _poleCooldown <= 0f)
                {
                    _poleCooldown = 0.58f;
                    _poleAnimTimer = 0.36f;
                    vFwd += isBoard ? 1.40f : 1.64f;
                    PlayClip(ProceduralAssets.SnowStepClip, 0.55f, 1.12f);
                }
                if (_poleAnimTimer > 0f) flags |= RideSyncFlags.Poling;

                // Braking / Hockey-stop when holding S (scaled proportionally for 10 m/s top speed)
                if (fwdInput < -0.2f)
                {
                    flags |= RideSyncFlags.Braking;
                    vFwd = Mathf.MoveTowards(vFwd, 0f, 5.8f * Mathf.Abs(fwdInput) * dt);
                    vLat = Mathf.MoveTowards(vLat, 0f, 6.5f * dt);
                }

                // Natural snow & aerodynamic drag (quadratic drag scaled by 1/0.4 -> 0.0035f so v(t) is 40% across all slopes)
                float baseDrag = (onSnow || onRamp != null) ? 0.042f : (UllrPlugin.AllowNonSnowRiding.Value ? 0.085f : 0.78f);
                if ((flags & RideSyncFlags.Tucking) != 0) baseDrag *= 0.65f;
                if (_chargingHop) flags |= RideSyncFlags.Tucking;

                float spd = Mathf.Sqrt(vFwd * vFwd + vLat * vLat);
                if (spd > 0.01f)
                {
                    float aeroDecel = (baseDrag * spd + 0.0035f * spd * spd) * dt;
                    float newSpd = Mathf.Min(maxSpeed, Mathf.Max(0f, spd - aeroDecel));
                    float ratio = newSpd / spd;
                    vFwd *= ratio;
                    vLat *= ratio;
                }

                Vector3 finalVel = slopeFwd * vFwd + slopeRight * vLat;
                // Gentle ground hug when not jumping
                if (onRamp == null && finalVel.y > -0.2f && vel.y <= 0.5f)
                {
                    finalVel -= groundNormal * 0.35f;
                }

                body.linearVelocity = finalVel;
                _prevPlanarVel = new Vector3(finalVel.x, 0f, finalVel.z);

                float targetLean = Mathf.Clamp(-steerInput * Mathf.Lerp(10f, 34f, spd / maxSpeed)
                                               + ((flags & RideSyncFlags.Braking) != 0 ? 22f : 0f), -38f, 38f);
                _leanAngle = Mathf.Lerp(_leanAngle, targetLean, dt * 9f);
            }
            else
            {
                // Mid-air tricks & subtle flight control!
                flags |= RideSyncFlags.Airborne;
                _wasAirborne = true;
                _airTime += dt;

                if (_airTime > 0.12f && Mathf.Abs(steerInput) > 0.1f)
                {
                    float spinRate = isBoard ? 320f : 290f;
                    float dSpin = steerInput * spinRate * dt;
                    _trickSpinYaw = Mathf.Repeat(_trickSpinYaw + dSpin, 360f);
                    _accumulatedSpin += dSpin;
                }

                bool grabHeld = _player.TakeInput() &&
                                (ZInput.GetButton("Block") || ZInput.GetButton("JoyBlock") || Input.GetKey(KeyCode.C));
                if (grabHeld && _airTime > 0.15f)
                {
                    flags |= RideSyncFlags.Grabbing;
                }

                // Slight air-drift control without killing launch speed (40% scaled)
                Vector3 v = body.linearVelocity;
                Quaternion headingRot = Quaternion.Euler(0f, _headingYaw, 0f);
                Vector3 airAdjust = (headingRot * new Vector3(steerInput * 0.65f, 0f, fwdInput * 0.72f)) * dt;
                Vector3 newHoriz = new Vector3(v.x + airAdjust.x, 0f, v.z + airAdjust.z);
                if (newHoriz.magnitude > maxSpeed * 1.15f)
                {
                    newHoriz = newHoriz.normalized * (maxSpeed * 1.15f);
                }
                body.linearVelocity = new Vector3(newHoriz.x, v.y, newHoriz.z);
                _prevPlanarVel = newHoriz;
                _leanAngle = Mathf.Lerp(_leanAngle, -steerInput * 16f, dt * 6f);
            }

            ApplyPlayerVisualAndAnim(body, grounded, steerInput, flags);
        }

        private void UpdateRopeTowMotion(Rigidbody body, float dt)
        {
            if (_activeTowStart == null || _activeTowStart.UphillTarget == null)
            {
                DetachFromTow(jumpPop: false);
                return;
            }

            Vector3 a = _activeTowStart.transform.position;
            Vector3 b = _activeTowStart.UphillTarget.transform.position;
            float spanLen = Mathf.Max(1f, Vector3.Distance(a, b));
            const float towSpeed = 4.6f; // 40% of original 11.5 m/s (~17 km/h uphill pull)

            _towProgress += (towSpeed / spanLen) * dt;
            if (_towProgress >= 0.98f)
            {
                // Seamlessly hand off to the next uphill span if chained!
                RunicRopeTow next = _activeTowStart.UphillTarget;
                next.RefreshUphillLink();
                if (next.UphillTarget != null)
                {
                    _activeTowStart = next;
                    _towProgress = 0.01f;
                    a = _activeTowStart.transform.position;
                    b = _activeTowStart.UphillTarget.transform.position;
                }
                else
                {
                    _player.Message(MessageHud.MessageType.Center, "Reached Summit Station — Shred it!");
                    PlayClip(ProceduralAssets.FrostChimeClip, 0.75f, 1.25f);
                    DetachFromTow(jumpPop: false);
                    return;
                }
            }

            Vector3 targetPos = Vector3.Lerp(a, b, _towProgress);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(targetPos, out float gh))
            {
                targetPos.y = Mathf.Max(targetPos.y, gh + 0.08f);
            }

            Vector3 dir = (b - a).normalized;
            Vector3 flatDir = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flatDir.sqrMagnitude > 0.01f)
            {
                _headingYaw = Quaternion.LookRotation(flatDir.normalized, Vector3.up).eulerAngles.y;
            }

            Vector3 desiredVel = (targetPos - _player.transform.position) / Mathf.Max(0.01f, dt);
            body.linearVelocity = Vector3.ClampMagnitude(desiredVel, towSpeed * 1.4f);
            _prevPlanarVel = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            _leanAngle = Mathf.Lerp(_leanAngle, 0f, dt * 8f);
            _trickSpinYaw = 0f;

            ApplyPlayerVisualAndAnim(body, true, 0f, RideSyncFlags.OnRopeTow);
        }

        private void PerformBunnyHop()
        {
            if (_player.m_body == null) return;
            float ratio = HopChargeRatio;
            bool isBoard = _equippedGear == RideGearType.Snowboard;
            float basePop = isBoard ? 4.8f : 4.5f;
            float maxPop = isBoard ? 7.0f : 6.5f;
            float pop = Mathf.Lerp(basePop, maxPop, ratio);

            Vector3 v = _player.m_body.linearVelocity;
            Vector3 jumpAxis = Vector3.Slerp(Vector3.up, _smoothedNormal, 0.28f).normalized;
            v += jumpAxis * pop;
            _player.m_body.linearVelocity = v;
            _wasAirborne = true;
            _airTime = 0.05f;
            _accumulatedSpin = 0f;
            PlayClip(ProceduralAssets.JumpPopClip, 0.75f, 1.08f);
        }

        private void EvaluateAirLanding(Rigidbody body, Vector3 groundNormal)
        {
            float absSpin = Mathf.Abs(_accumulatedSpin);
            // Measure how sideways the trick spin left the board/skis (0 or 180 is clean twin-tip stomp; ~90 or ~270 is sideways!)
            float mod180 = Mathf.Repeat(_trickSpinYaw, 180f);
            float offAxis = Mathf.Min(mod180, 180f - mod180); // 0..90

            Vector3 v = body.linearVelocity;
            float speed = new Vector3(v.x, 0f, v.z).magnitude;

            if (_airTime > 0.75f && speed > 4.4f && offAxis > 62f)
            {
                TriggerWipeout("Caught an edge on landing!");
                return;
            }

            // Clean stomp! Convert downward velocity smoothly onto the landing slope!
            Vector3 projected = Vector3.ProjectOnPlane(v, groundNormal);
            if (v.y < -3f && groundNormal.y < 0.96f)
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                float absorbed = Mathf.Min(3.0f, Mathf.Abs(v.y) * 0.20f);
                projected += downhill * absorbed;
            }

            // Snap twin-tip 180s cleanly and celebrate spins!
            if (mod180 > 90f)
            {
                // Landed 180 switch -> smoothly align forward heading to velocity if moving fast
                Vector3 flatProj = new Vector3(projected.x, 0f, projected.z);
                if (flatProj.sqrMagnitude > 0.64f)
                {
                    _headingYaw = Quaternion.LookRotation(flatProj.normalized, Vector3.up).eulerAngles.y;
                }
            }
            _trickSpinYaw = 0f;

            if (absSpin >= 145f)
            {
                int roundedSpin = Mathf.RoundToInt(absSpin / 180f) * 180;
                if (roundedSpin >= 180)
                {
                    bool hadGrab = (SyncedFlags & RideSyncFlags.Grabbing) != 0;
                    string trickLabel = hadGrab
                        ? $"Clean {roundedSpin}° Frost Grab! (+Speed Boost)"
                        : $"Stomped {roundedSpin}° Spin!";
                    _player.Message(MessageHud.MessageType.TopLeft, trickLabel);
                    if (projected.sqrMagnitude > 0.25f)
                    {
                        projected += projected.normalized * (hadGrab ? 1.04f : 0.64f);
                    }
                    PlayClip(ProceduralAssets.FrostChimeClip, 0.75f, 1.25f);
                }
            }
            else if (_airTime > 0.55f)
            {
                PlayClip(ProceduralAssets.SnowStepClip, 0.75f, 0.9f);
            }

            body.linearVelocity = projected;
        }

        public void TriggerWipeout(string reason)
        {
            _wipeoutTimer = 1.35f;
            _chargingHop = false;
            _hopCharge = 0f;
            _activeTowStart = null;

            _player.Message(MessageHud.MessageType.Center, $"WIPEOUT! {reason}");
            PlayClip(ProceduralAssets.CrashClip, 0.9f, 0.85f);

            // Non-lethal minor bruise only on very hard crashes so wipeouts stay hilarious with friends
            if (_player.GetHealth() > 12f)
            {
                HitData hit = new HitData();
                hit.m_damage.m_blunt = Mathf.Min(7f, _player.GetHealth() - 8f);
                hit.m_point = _player.transform.position;
                hit.m_hitType = HitData.HitType.Fall;
                _player.Damage(hit);
            }
        }

        private void ApplyPlayerVisualAndAnim(Rigidbody body, bool grounded, float steerInput, RideSyncFlags flags)
        {
            Quaternion targetRot = Quaternion.Euler(0f, _headingYaw, 0f);
            body.rotation = targetRot;
            body.angularVelocity = Vector3.zero;
            _player.UpdateEyeRotation();

            float spd = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z).magnitude;
            _player.m_currentVel = body.linearVelocity;

            if (_player.m_zanim != null)
            {
                // Keep legs calm/poised instead of frantic sprinting leg-flailing!
                float animFwd = (flags & RideSyncFlags.Poling) != 0 ? 2.2f : 0.0f;
                _player.m_zanim.SetFloat(AnimForwardSpeed, animFwd);
                _player.m_zanim.SetFloat(AnimSidewaySpeed, 0f);
                _player.m_zanim.SetFloat(AnimTurnSpeed, 0f);
                _player.m_zanim.SetBool(AnimInWater, false);
                _player.m_zanim.SetBool(AnimOnGround, true);
                _player.m_zanim.SetBool(AnimFalling, false);
                _player.m_zanim.SetBool(AnimEncumbered, false);
            }

            SyncedRideMode = _equippedGear;
            SyncedLeanAngle = _leanAngle;
            SyncedSpinYaw = _trickSpinYaw;
            SyncedFlags = flags;
            SyncedSpeed = spd;
        }

        private void SyncOwnerToZdo(float dt, bool force)
        {
            ZDO zdo = _nview?.GetZDO();
            if (zdo == null) return;

            if (!_ridingActive)
            {
                if (force || zdo.GetInt(ZdoRideMode) != 0)
                {
                    zdo.Set(ZdoRideMode, 0);
                    zdo.Set(ZdoFlags, 0);
                    zdo.Set(ZdoSpeed, 0f);
                }
                return;
            }

            _zdoSyncTimer -= dt;
            if (!force && _zdoSyncTimer > 0f) return;
            _zdoSyncTimer = 0.05f; // Crisp 20Hz multiplayer sync

            zdo.Set(ZdoRideMode, (int)SyncedRideMode);
            zdo.Set(ZdoLean, SyncedLeanAngle);
            zdo.Set(ZdoSpin, SyncedSpinYaw);
            zdo.Set(ZdoFlags, (int)SyncedFlags);
            zdo.Set(ZdoSpeed, SyncedSpeed);
        }

        private void ReadRemoteFromZdo(float dt)
        {
            ZDO zdo = _nview?.GetZDO();
            if (zdo == null) return;

            int utilHash = zdo.GetInt(ZDOVars.s_utilityItem);
            if (utilHash == SkisPrefabName.GetStableHashCode())
                _equippedGear = RideGearType.Skis;
            else if (utilHash == SnowboardPrefabName.GetStableHashCode())
                _equippedGear = RideGearType.Snowboard;
            else
                _equippedGear = RideGearType.None;

            SyncedRideMode = (RideGearType)zdo.GetInt(ZdoRideMode);
            float targetLean = zdo.GetFloat(ZdoLean);
            float targetSpin = zdo.GetFloat(ZdoSpin);
            SyncedLeanAngle = Mathf.Lerp(SyncedLeanAngle, targetLean, dt * 14f);
            SyncedSpinYaw = Mathf.LerpAngle(SyncedSpinYaw, targetSpin, dt * 16f);
            SyncedFlags = (RideSyncFlags)zdo.GetInt(ZdoFlags);
            SyncedSpeed = Mathf.Lerp(SyncedSpeed, zdo.GetFloat(ZdoSpeed), dt * 12f);
        }

        private RideGearType DetectEquippedGearFromPlayer()
        {
            ItemDrop.ItemData util = _player.m_utilityItem;
            if (util == null) return RideGearType.None;
            string pName = util.m_dropPrefab != null ? util.m_dropPrefab.name : util.m_shared?.m_name;
            if (string.IsNullOrEmpty(pName)) return RideGearType.None;

            if (pName == SkisPrefabName || util.m_shared.m_name == "$item_ullr_skis" || util.m_shared.m_name.Contains("Ullr's"))
                return RideGearType.Skis;
            if (pName == SnowboardPrefabName || util.m_shared.m_name == "$item_moder_snowboard" || util.m_shared.m_name.Contains("Moder's"))
                return RideGearType.Snowboard;

            return RideGearType.None;
        }

        private bool IsGroundedForSki()
        {
            return _player.IsOnGround() || SampleGround(out _, out _, out _);
        }

        private bool CanStartRidingHere(out string reason)
        {
            reason = string.Empty;
            if (UllrPlugin.AllowNonSnowRiding.Value) return true;
            if (IsOnSnowOrRamp(out _)) return true;
            if (RunicRopeTow.TryFindNearestTowSpan(_player.transform.position, 4.0f, out _, out _, out _)) return true;

            reason = "Need Mountain snow, snowy weather, or a Runic Ramp! (Toggle 'AllowNonSnowRiding' in config for grass)";
            return false;
        }

        private bool IsOnSnowOrRamp(out float slopeDeg)
        {
            SampleGround(out Vector3 normal, out bool onSnow, out RunicJumpRamp ramp);
            slopeDeg = Vector3.Angle(Vector3.up, normal);
            return onSnow || ramp != null;
        }

        private bool SampleGround(out Vector3 groundNormal, out bool onSnow, out RunicJumpRamp ramp)
        {
            groundNormal = Vector3.up;
            onSnow = IsSnowEnvironmentOrBiome(_player.transform.position);
            ramp = null;

            Collider lastCol = _player.GetLastGroundCollider();
            if (_player.IsOnGround() && lastCol != null)
            {
                groundNormal = _player.m_lastGroundNormal.sqrMagnitude > 0.1f ? _player.m_lastGroundNormal.normalized : Vector3.up;
                if (RunicJumpRamp.TryGetRamp(lastCol, out ramp))
                {
                    onSnow = true;
                }
                return true;
            }

            Vector3 origin = _player.transform.position + Vector3.up * 0.65f;
            int mask = LayerMask.GetMask("terrain", "Default", "static_solid", "piece");
            if (UnityEngine.Physics.SphereCast(origin, 0.28f, Vector3.down, out RaycastHit hit, 0.92f, mask, QueryTriggerInteraction.Ignore))
            {
                groundNormal = hit.normal.normalized;
                if (RunicJumpRamp.TryGetRamp(hit.collider, out ramp))
                {
                    onSnow = true;
                }
                return true;
            }

            return false;
        }

        public static bool IsSnowEnvironmentOrBiome(Vector3 worldPos)
        {
            if (Heightmap.FindBiome(worldPos) is var hmBiome &&
                (hmBiome == Heightmap.Biome.Mountain || hmBiome == Heightmap.Biome.DeepNorth))
            {
                return true;
            }

            if (WorldGenerator.instance != null)
            {
                Heightmap.Biome wgBiome = WorldGenerator.instance.GetBiome(worldPos.x, worldPos.z);
                if (wgBiome == Heightmap.Biome.Mountain || wgBiome == Heightmap.Biome.DeepNorth)
                    return true;
            }

            if (EnvMan.instance != null)
            {
                EnvSetup env = EnvMan.instance.GetCurrentEnvironment();
                if (env != null && !string.IsNullOrEmpty(env.m_name))
                {
                    string n = env.m_name.ToLowerInvariant();
                    if (n.Contains("snow") || n.Contains("twilight_snow") || n.Contains("winter") || n.Contains("frost"))
                        return true;
                }
            }
            return false;
        }

        private static float GetSteerInput()
        {
            float s = 0f;
            if (ZInput.GetButton("Right")) s += 1f;
            if (ZInput.GetButton("Left")) s -= 1f;
            float joy = ZInput.GetJoyLeftStickX();
            if (Mathf.Abs(joy) > 0.12f) s += joy;
            return Mathf.Clamp(s, -1f, 1f);
        }

        private static float GetMoveForwardInput()
        {
            float f = 0f;
            if (ZInput.GetButton("Forward")) f += 1f;
            if (ZInput.GetButton("Backward")) f -= 1f;
            float joy = -ZInput.GetJoyLeftStickY();
            if (Mathf.Abs(joy) > 0.12f) f += joy;
            return Mathf.Clamp(f, -1f, 1f);
        }

        private void OnGearNewlyEquipped(RideGearType gear)
        {
            string keyLabel = UllrPlugin.ToggleRideKey.Value.ToString();
            _player.Message(MessageHud.MessageType.TopLeft,
                $"Ullr's Frost Ward active! Press [{keyLabel}] on snow to shred (Hammer auto-stows)");

            if (!_peakMarkedThisSession && UllrPlugin.AutoMarkNearestPeak.Value)
            {
                _peakMarkedThisSession = true;
                TryMarkNearestMountainSummit();
            }
        }

        private void TryMarkNearestMountainSummit()
        {
            if (WorldGenerator.instance == null || Minimap.instance == null) return;

            Vector3 origin = _player.transform.position;
            Vector3 bestPeak = Vector3.zero;
            float bestScore = -1f;
            float bestHeight = 0f;

            // Coarse radial scan within 2600m to locate the grandest nearby ski peak!
            for (float r = 250f; r <= 2600f; r += 90f)
            {
                int steps = Mathf.Max(16, Mathf.RoundToInt(r / 45f));
                for (int i = 0; i < steps; i++)
                {
                    float ang = (i / (float)steps) * Mathf.PI * 2f;
                    float wx = origin.x + Mathf.Cos(ang) * r;
                    float wz = origin.z + Mathf.Sin(ang) * r;
                    if (WorldGenerator.instance.GetBiome(wx, wz) != Heightmap.Biome.Mountain) continue;

                    float h = WorldGenerator.instance.GetHeight(wx, wz);
                    if (h < 110f) continue;
                    // Favor towering elevation while preferring closer walkable peaks
                    float score = h * 2.2f - r * 0.08f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHeight = h;
                        bestPeak = new Vector3(wx, h, wz);
                    }
                }
            }

            if (bestScore > 0f)
            {
                string pinLabel = $"Ullr's Summit ({Mathf.RoundToInt(bestHeight)}m)";
                bool alreadyPinned = false;
                if (Minimap.instance.m_pins != null)
                {
                    foreach (var p in Minimap.instance.m_pins)
                    {
                        if (p != null && p.m_name != null && p.m_name.StartsWith("Ullr's Summit"))
                        {
                            alreadyPinned = true;
                            break;
                        }
                    }
                }
                if (!alreadyPinned)
                {
                    Minimap.instance.AddPin(bestPeak, Minimap.PinType.Icon3, pinLabel, save: true, isChecked: false);
                    float dist = Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(bestPeak.x, bestPeak.z));
                    _player.Message(MessageHud.MessageType.Center,
                        $"Marked {pinLabel} on your map ({Mathf.RoundToInt(dist)}m away)!");
                }
            }
        }

        private void PlayClip(AudioClip clip, float volume, float pitch)
        {
            if (ProceduralAssets.IsHeadless || clip == null || _player == null) return;
            AudioSource.PlayClipAtPoint(clip, _player.transform.position + Vector3.up * 0.5f, volume);
        }

        private void OnGUI()
        {
            if (ProceduralAssets.IsHeadless || !UllrPlugin.ShowRidingHud.Value) return;
            if (_player == null || _player != Player.m_localPlayer || !_ridingActive) return;
            if (Hud.instance == null || Minimap.IsOpen() || InventoryGui.IsVisible() || Menu.IsVisible()) return;

            EnsureHudStyles();

            float kmh = SyncedSpeed * 3.6f;
            string gearLabel = _equippedGear == RideGearType.Snowboard ? "MODER'S SNOWBOARD" : "ULLR'S SKIS";
            string statusTag = "";
            if ((SyncedFlags & RideSyncFlags.OnRopeTow) != 0) statusTag = "  •  ROPE TOW";
            else if ((SyncedFlags & RideSyncFlags.WipedOut) != 0) statusTag = "  •  WIPEOUT!";
            else if ((SyncedFlags & RideSyncFlags.Airborne) != 0)
            {
                int deg = Mathf.RoundToInt(CurrentAirSpinDegrees);
                string grab = (SyncedFlags & RideSyncFlags.Grabbing) != 0 ? " + GRAB" : "";
                statusTag = deg >= 45 ? $"  •  AIR {deg}°{grab}" : $"  •  AIRBORNE{grab}";
            }
            else if ((SyncedFlags & RideSyncFlags.Braking) != 0) statusTag = "  •  CARVE BRAKE";
            else if ((SyncedFlags & RideSyncFlags.Tucking) != 0) statusTag = "  •  AERO TUCK";

            float w = 320f;
            float h = SlalomRaceTracker.IsRaceActive ? 74f : 54f;
            Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height - 156f, w, h);
            GUI.Box(box, GUIContent.none, _hudBoxStyle);

            GUI.Label(new Rect(box.x, box.y + 6f, box.width, 24f),
                $"{gearLabel}  •  {kmh:0} km/h{statusTag}", _hudTitleStyle);

            string hint = SlalomRaceTracker.IsRaceActive
                ? $"SLALOM {SlalomRaceTracker.CurrentElapsed:0.00}s  |  Gates: {SlalomRaceTracker.CheckpointsCleared}  |  Peak: {SlalomRaceTracker.PeakSpeedKmh:0} km/h"
                : (_chargingHop
                    ? $"POP CHARGE: {Mathf.RoundToInt(HopChargeRatio * 100f)}% (Release Space!)"
                    : $"[W] Tuck/Push  [S] Brake  [Space] Hop  [RMB] Air Grab  [{UllrPlugin.ToggleRideKey.Value}] Unclip");

            GUI.Label(new Rect(box.x, box.y + 30f, box.width, 22f), hint, _hudSubStyle);
        }

        private void EnsureHudStyles()
        {
            if (_hudBoxStyle != null) return;
            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.04f, 0.09f, 0.15f, 0.72f));
            bg.Apply();

            _hudBoxStyle = new GUIStyle(GUI.skin.box);
            _hudBoxStyle.normal.background = bg;

            _hudTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _hudTitleStyle.normal.textColor = new Color(0.45f, 0.95f, 1.0f, 1f);

            _hudSubStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            _hudSubStyle.normal.textColor = new Color(0.84f, 0.93f, 1.0f, 0.92f);
        }
    }
}
