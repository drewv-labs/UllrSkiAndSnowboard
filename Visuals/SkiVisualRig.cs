using UnityEngine;
using UnityEngine.Rendering;
using UllrSkiAndSnowboard.Assets;
using UllrSkiAndSnowboard.Physics;

namespace UllrSkiAndSnowboard.Visuals
{
    public class SkiVisualRig : MonoBehaviour
    {
        private Player _player;
        private VisEquipment _visEquip;
        private PlayerSkiController _controller;

        // Underfoot active riding rig
        private GameObject _activeRoot;
        private GameObject _skisGroup;
        private GameObject _snowboardGroup;
        private TrailRenderer _leftTrail;
        private TrailRenderer _rightTrail;
        private ParticleSystem _snowSpray;
        private Light _frostAuraLight;

        // Hand-held ski poles
        private GameObject _leftPoleObj;
        private GameObject _rightPoleObj;

        // Back-mounted stowed rig
        private GameObject _stowedBackRoot;
        private GameObject _stowedSkis;
        private GameObject _stowedBoard;

        // Continuous snow-carve audio timer
        private float _carveStepTimer;
        private Quaternion _smoothedSlopeAlign = Quaternion.identity;
        private bool _wasRidingVisualApplied;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _visEquip = GetComponent<VisEquipment>();
            _controller = GetComponent<PlayerSkiController>();
        }

        private void Start()
        {
            if (ProceduralAssets.IsHeadless) return;
            BuildVisualHierarchy();
        }

        private void OnDestroy()
        {
            if (_leftPoleObj != null) Destroy(_leftPoleObj);
            if (_rightPoleObj != null) Destroy(_rightPoleObj);
            if (_stowedBackRoot != null) Destroy(_stowedBackRoot);
        }

        private void BuildVisualHierarchy()
        {
            if (_activeRoot != null) return;

            _activeRoot = new GameObject("UllrActiveRideRig");
            _activeRoot.transform.SetParent(transform, false);
            _activeRoot.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            // 1. Twin Mythic Skis
            _skisGroup = new GameObject("TwinSkisGroup");
            _skisGroup.transform.SetParent(_activeRoot.transform, false);
            CreateSingleSkiVisual(_skisGroup.transform, new Vector3(-0.19f, 0f, 0.04f));
            CreateSingleSkiVisual(_skisGroup.transform, new Vector3(0.19f, 0f, 0.04f));

            // 2. Moder's Drakkar Snowboard
            _snowboardGroup = new GameObject("SnowboardGroup");
            _snowboardGroup.transform.SetParent(_activeRoot.transform, false);
            CreateSnowboardVisual(_snowboardGroup.transform, new Vector3(0f, 0f, 0.02f));

            // 3. Frost Ribbon Trails
            _leftTrail = CreateFrostTrail(_activeRoot.transform, new Vector3(-0.18f, 0.04f, -0.78f));
            _rightTrail = CreateFrostTrail(_activeRoot.transform, new Vector3(0.18f, 0.04f, -0.78f));

            // 4. Snow Spray Particle System
            _snowSpray = CreateSnowSpraySystem(_activeRoot.transform);

            // 5. Mythic Frost Aura Light
            GameObject lightGo = new GameObject("UllrFrostGlow");
            lightGo.transform.SetParent(_activeRoot.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.35f, 0.1f);
            _frostAuraLight = lightGo.AddComponent<Light>();
            _frostAuraLight.type = LightType.Point;
            _frostAuraLight.color = new Color(0.30f, 0.90f, 1.0f);
            _frostAuraLight.range = 4.4f;
            _frostAuraLight.intensity = 0.85f;
            _frostAuraLight.shadows = LightShadows.None;

            // 6. Ski Poles attached to player hands if available
            if (_visEquip != null)
            {
                if (_visEquip.m_leftHand != null)
                {
                    _leftPoleObj = CreatePoleInstance("UllrLeftSkiPole", _visEquip.m_leftHand);
                }
                if (_visEquip.m_rightHand != null)
                {
                    _rightPoleObj = CreatePoleInstance("UllrRightSkiPole", _visEquip.m_rightHand);
                }
                Transform backAnchor = _visEquip.m_backTool != null ? _visEquip.m_backTool : (_player.m_visual != null ? _player.m_visual.transform : null);
                if (backAnchor != null)
                {
                    BuildStowedBackRig(backAnchor);
                }
            }

            _activeRoot.SetActive(false);
        }

        private void BuildStowedBackRig(Transform backAnchor)
        {
            _stowedBackRoot = new GameObject("UllrStowedBackGear");
            _stowedBackRoot.transform.SetParent(backAnchor, false);
            // Adjust depending on whether attached to m_backTool bone or m_visual root
            if (_visEquip != null && backAnchor == _visEquip.m_backTool)
            {
                _stowedBackRoot.transform.localPosition = new Vector3(0f, 0f, -0.06f);
                _stowedBackRoot.transform.localRotation = Quaternion.Euler(78f, 18f, 25f);
            }
            else
            {
                _stowedBackRoot.transform.localPosition = new Vector3(0f, 1.15f, -0.24f);
                _stowedBackRoot.transform.localRotation = Quaternion.Euler(82f, 0f, 28f);
            }
            _stowedBackRoot.transform.localScale = Vector3.one * 0.78f;

            _stowedSkis = new GameObject("StowedSkis");
            _stowedSkis.transform.SetParent(_stowedBackRoot.transform, false);
            CreateSingleSkiVisual(_stowedSkis.transform, new Vector3(-0.07f, 0f, 0f));
            CreateSingleSkiVisual(_stowedSkis.transform, new Vector3(0.07f, 0.025f, 0f));

            _stowedBoard = new GameObject("StowedBoard");
            _stowedBoard.transform.SetParent(_stowedBackRoot.transform, false);
            CreateSnowboardVisual(_stowedBoard.transform, Vector3.zero);

            _stowedBackRoot.SetActive(false);
        }

        private static void CreateSingleSkiVisual(Transform parent, Vector3 localPos)
        {
            GameObject ski = new GameObject("MythicSki");
            ski.transform.SetParent(parent, false);
            ski.transform.localPosition = localPos;

            AddMeshChild(ski.transform, "Deck", ProceduralAssets.SkiMesh, ProceduralAssets.DarkWoodMat);
            AddMeshChild(ski.transform, "RuneInlay", ProceduralAssets.SkiRuneMesh, ProceduralAssets.FrostRuneMat);

            // Binding block underfoot
            GameObject binding = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(binding.GetComponent<Collider>());
            binding.name = "Binding";
            binding.transform.SetParent(ski.transform, false);
            binding.transform.localPosition = new Vector3(0f, 0.032f, -0.04f);
            binding.transform.localScale = new Vector3(0.088f, 0.028f, 0.26f);
            binding.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.SilverEdgeMat;
        }

        private static void CreateSnowboardVisual(Transform parent, Vector3 localPos)
        {
            GameObject board = new GameObject("ModerDrakkarBoard");
            board.transform.SetParent(parent, false);
            board.transform.localPosition = localPos;

            AddMeshChild(board.transform, "Deck", ProceduralAssets.SnowboardMesh, ProceduralAssets.SilverEdgeMat);
            AddMeshChild(board.transform, "RuneSpine", ProceduralAssets.SnowboardRuneMesh, ProceduralAssets.FrostRuneMat);

            // Front & Rear Viking Boot Bindings
            float[] zPositions = { -0.26f, 0.26f };
            float[] angles = { -12f, 15f };
            for (int i = 0; i < 2; i++)
            {
                GameObject strap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(strap.GetComponent<Collider>());
                strap.name = "BoardBinding";
                strap.transform.SetParent(board.transform, false);
                strap.transform.localPosition = new Vector3(0f, 0.034f, zPositions[i]);
                strap.transform.localRotation = Quaternion.Euler(0f, angles[i], 0f);
                strap.transform.localScale = new Vector3(0.24f, 0.032f, 0.13f);
                strap.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.LeatherMat;
            }
        }

        private static GameObject CreatePoleInstance(string name, Transform handBone)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(handBone, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.Euler(-55f, 0f, 0f);

            AddMeshChild(root.transform, "PoleShaft", ProceduralAssets.PoleMesh, ProceduralAssets.SilverEdgeMat);

            GameObject pommel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(pommel.GetComponent<Collider>());
            pommel.name = "RunePommel";
            pommel.transform.SetParent(root.transform, false);
            pommel.transform.localPosition = new Vector3(0f, 0.13f, 0f);
            pommel.transform.localScale = Vector3.one * 0.048f;
            pommel.GetComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.FrostRuneMat;

            root.SetActive(false);
            return root;
        }

        private static GameObject AddMeshChild(Transform parent, string name, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        private static TrailRenderer CreateFrostTrail(Transform parent, Vector3 localPos)
        {
            GameObject go = new GameObject("FrostRibbonTrail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.65f;
            tr.minVertexDistance = 0.15f;
            tr.startWidth = 0.11f;
            tr.endWidth = 0.01f;
            tr.sharedMaterial = ProceduralAssets.TrailRibbonMat;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.startColor = new Color(0.45f, 0.94f, 1f, 0.72f);
            tr.endColor = new Color(0.75f, 0.98f, 1f, 0.0f);
            tr.emitting = false;
            return tr;
        }

        private static ParticleSystem CreateSnowSpraySystem(Transform parent)
        {
            GameObject go = new GameObject("SnowCarveSpray");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, -0.55f);
            go.transform.localRotation = Quaternion.Euler(-25f, 180f, 0f);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 5.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.34f);
            main.startColor = new Color(0.90f, 0.97f, 1f, 0.80f);
            main.gravityModifier = 0.65f;
            main.maxParticles = 220;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.22f;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = ProceduralAssets.SnowParticleMat;
            psr.shadowCastingMode = ShadowCastingMode.Off;

            return ps;
        }

        private void LateUpdate()
        {
            if (ProceduralAssets.IsHeadless || _controller == null || _player == null) return;
            if (_activeRoot == null) BuildVisualHierarchy();
            if (_activeRoot == null) return;

            RideGearType equipped = _controller.EquippedGear;
            RideGearType activeMode = _controller.SyncedRideMode;
            bool isDead = _player.IsDead();

            if (isDead || equipped == RideGearType.None)
            {
                SetAllVisualsOff();
                return;
            }

            if (activeMode == RideGearType.None)
            {
                // Walking / building on the mountain: show stowed gear on back!
                if (_activeRoot.activeSelf) _activeRoot.SetActive(false);
                if (_leftPoleObj != null && _leftPoleObj.activeSelf) _leftPoleObj.SetActive(false);
                if (_rightPoleObj != null && _rightPoleObj.activeSelf) _rightPoleObj.SetActive(false);

                RestorePlayerBodyStanceIfNeeded();

                if (_stowedBackRoot != null)
                {
                    if (!_stowedBackRoot.activeSelf) _stowedBackRoot.SetActive(true);
                    _stowedSkis.SetActive(equipped == RideGearType.Skis);
                    _stowedBoard.SetActive(equipped == RideGearType.Snowboard);
                }
                return;
            }

            // Actively Skiing or Snowboarding!
            if (_stowedBackRoot != null && _stowedBackRoot.activeSelf) _stowedBackRoot.SetActive(false);
            if (!_activeRoot.activeSelf) _activeRoot.SetActive(true);

            bool isBoard = activeMode == RideGearType.Snowboard;
            _skisGroup.SetActive(!isBoard);
            _snowboardGroup.SetActive(isBoard);

            float lean = _controller.SyncedLeanAngle;
            float spinYaw = _controller.SyncedSpinYaw;
            RideSyncFlags flags = _controller.SyncedFlags;
            float speed = _controller.SyncedSpeed;
            bool airborne = (flags & RideSyncFlags.Airborne) != 0;
            bool grabbing = (flags & RideSyncFlags.Grabbing) != 0;
            bool braking = (flags & RideSyncFlags.Braking) != 0;
            bool tucking = (flags & RideSyncFlags.Tucking) != 0;
            bool wipedOut = (flags & RideSyncFlags.WipedOut) != 0;
            bool poling = (flags & RideSyncFlags.Poling) != 0;

            // Align ski/board platform to ground normal when grounded
            Quaternion desiredSlopeLocal = Quaternion.identity;
            if (!airborne && UnityEngine.Physics.Raycast(transform.position + Vector3.up * 0.7f, Vector3.down, out RaycastHit hit, 1.4f,
                    LayerMask.GetMask("terrain", "Default", "static_solid", "piece"), QueryTriggerInteraction.Ignore))
            {
                Vector3 localNormal = transform.InverseTransformDirection(hit.normal);
                Vector3 localFwd = Vector3.ProjectOnPlane(Vector3.forward, localNormal).normalized;
                if (localFwd.sqrMagnitude > 0.01f)
                {
                    desiredSlopeLocal = Quaternion.LookRotation(localFwd, localNormal);
                }
            }
            _smoothedSlopeAlign = Quaternion.Slerp(_smoothedSlopeAlign, desiredSlopeLocal, Time.deltaTime * 14f);

            float extraYaw = spinYaw + (braking ? (isBoard ? 58f : 26f) : 0f);
            float grabPitch = grabbing ? -16f : 0f;
            float wipeRoll = wipedOut ? 72f : 0f;

            _activeRoot.transform.localRotation =
                _smoothedSlopeAlign * Quaternion.Euler(grabPitch, extraYaw, lean + wipeRoll);

            // Position trails narrow on snowboard, wider on twin skis
            if (_leftTrail != null && _rightTrail != null)
            {
                float xOff = isBoard ? 0.11f : 0.19f;
                _leftTrail.transform.localPosition = new Vector3(-xOff, 0.04f, -0.76f);
                _rightTrail.transform.localPosition = new Vector3(xOff, 0.04f, -0.76f);
                bool emitTrail = (!airborne && speed > 2.2f) || grabbing;
                _leftTrail.emitting = emitTrail;
                _rightTrail.emitting = emitTrail;
            }

            // Pose the Viking character mesh (sideways snowboarder crouch, downhill ski tuck, air grab, or wipeout!)
            if (_player.m_visual != null)
            {
                _wasRidingVisualApplied = true;
                float stanceYaw = extraYaw + (isBoard ? 72f : 0f);
                float crouchPitch = tucking ? 18f : (grabbing ? 24f : (isBoard ? 8f : 6f));
                float bodyRoll = lean * 0.78f + (wipedOut ? 68f : 0f);
                float crouchDrop = (tucking || grabbing) ? -0.14f : -0.04f;

                _player.m_visual.transform.localPosition = new Vector3(0f, crouchDrop, 0f);
                _player.m_visual.transform.localRotation =
                    _smoothedSlopeAlign * Quaternion.Euler(crouchPitch, stanceYaw, bodyRoll);
            }

            // Show Runic Ski Poles in hands when skiing (and weapons aren't drawn)
            bool showPoles = !isBoard && !wipedOut;
            if (_leftPoleObj != null)
            {
                if (_leftPoleObj.activeSelf != showPoles) _leftPoleObj.SetActive(showPoles);
                if (showPoles)
                {
                    float swing = poling ? Mathf.Sin(Time.time * 18f) * 32f : (tucking ? -35f : 0f);
                    _leftPoleObj.transform.localRotation = Quaternion.Euler(-55f + swing, 0f, 0f);
                }
            }
            if (_rightPoleObj != null)
            {
                if (_rightPoleObj.activeSelf != showPoles) _rightPoleObj.SetActive(showPoles);
                if (showPoles)
                {
                    float swing = poling ? Mathf.Sin(Time.time * 18f) * 32f : (tucking ? -35f : 0f);
                    _rightPoleObj.transform.localRotation = Quaternion.Euler(-55f + swing, 0f, 0f);
                }
            }

            // Snow spray & aura intensity
            if (_snowSpray != null)
            {
                var emission = _snowSpray.emission;
                if (!airborne && speed > 2.5f)
                {
                    if (!_snowSpray.isPlaying) _snowSpray.Play();
                    float carveFactor = 1f + Mathf.Abs(lean) * 0.06f + (braking ? 2.6f : 0f) + (wipedOut ? 3.0f : 0f);
                    emission.rateOverTime = Mathf.Clamp(speed * 3.8f * carveFactor, 0f, 160f);
                }
                else
                {
                    emission.rateOverTime = 0f;
                }
            }

            if (_frostAuraLight != null)
            {
                _frostAuraLight.intensity = Mathf.Lerp(0.65f, 1.55f, Mathf.Clamp01(speed / 24f)) + (grabbing ? 0.5f : 0f);
            }

            // Rhythmic snow-carve audio pulse on grounded movement
            if (!airborne && speed > 3.2f && ProceduralAssets.SnowStepClip != null)
            {
                _carveStepTimer -= Time.deltaTime * Mathf.Lerp(1.2f, 4.2f, Mathf.Clamp01(speed / 24f));
                if (_carveStepTimer <= 0f)
                {
                    _carveStepTimer = 0.42f;
                    float vol = Mathf.Lerp(0.08f, 0.28f, Mathf.Clamp01(speed / 24f)) + (braking ? 0.18f : 0f);
                    AudioSource.PlayClipAtPoint(ProceduralAssets.SnowStepClip, transform.position, vol);
                }
            }
        }

        private void SetAllVisualsOff()
        {
            if (_activeRoot != null && _activeRoot.activeSelf) _activeRoot.SetActive(false);
            if (_stowedBackRoot != null && _stowedBackRoot.activeSelf) _stowedBackRoot.SetActive(false);
            if (_leftPoleObj != null && _leftPoleObj.activeSelf) _leftPoleObj.SetActive(false);
            if (_rightPoleObj != null && _rightPoleObj.activeSelf) _rightPoleObj.SetActive(false);
            RestorePlayerBodyStanceIfNeeded();
        }

        private void RestorePlayerBodyStanceIfNeeded()
        {
            if (!_wasRidingVisualApplied) return;
            _wasRidingVisualApplied = false;
            if (_player != null && _player.m_visual != null)
            {
                _player.m_visual.transform.localPosition = Vector3.zero;
                _player.m_visual.transform.localRotation = Quaternion.identity;
            }
        }
    }
}
