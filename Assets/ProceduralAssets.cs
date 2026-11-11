using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace UllrSkiAndSnowboard.Assets
{
    public static class ProceduralAssets
    {
        public static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private static Material _darkWoodMat;
        private static Material _silverEdgeMat;
        private static Material _frostRuneMat;
        private static Material _snowDeckMat;
        private static Material _leatherMat;
        private static Material _trailRibbonMat;
        private static Material _snowParticleMat;

        private static AudioClip _snowStepClip;
        private static AudioClip _frostChimeClip;
        private static AudioClip _crashClip;
        private static AudioClip _jumpPopClip;

        private static Mesh _skiMesh;
        private static Mesh _skiRuneMesh;
        private static Mesh _poleMesh;
        private static Mesh _snowboardMesh;
        private static Mesh _snowboardRuneMesh;
        private static Mesh _rampDeckMesh;
        private static Mesh _rampRailMesh;

        public static Sprite SkisIcon { get; private set; }
        public static Sprite SnowboardIcon { get; private set; }
        public static Sprite FrostWardIcon { get; private set; }
        public static Sprite RampIcon { get; private set; }
        public static Sprite GateIcon { get; private set; }
        public static Sprite RopeTowIcon { get; private set; }

        public static void Initialize()
        {
            BuildIcons();
            HarvestMaterialsAndAudio();
            BuildMeshes();
        }

        public static Material DarkWoodMat => _darkWoodMat ?? CreateFallbackMaterial(new Color(0.24f, 0.16f, 0.10f), 0f, 0.35f, Color.black);
        public static Material SilverEdgeMat => _silverEdgeMat ?? CreateFallbackMaterial(new Color(0.78f, 0.86f, 0.94f), 0.85f, 0.78f, new Color(0.05f, 0.14f, 0.22f));
        public static Material FrostRuneMat => _frostRuneMat ?? CreateFallbackMaterial(new Color(0.35f, 0.92f, 1.0f), 0.2f, 0.9f, new Color(0.22f, 0.88f, 1.0f) * 2.4f);
        public static Material SnowDeckMat => _snowDeckMat ?? CreateFallbackMaterial(new Color(0.84f, 0.93f, 0.99f), 0.05f, 0.65f, new Color(0.04f, 0.10f, 0.16f));
        public static Material LeatherMat => _leatherMat ?? CreateFallbackMaterial(new Color(0.34f, 0.20f, 0.11f), 0.05f, 0.25f, Color.black);
        public static Material TrailRibbonMat => _trailRibbonMat ?? FrostRuneMat;
        public static Material SnowParticleMat => _snowParticleMat ?? SnowDeckMat;

        public static AudioClip SnowStepClip => _snowStepClip;
        public static AudioClip FrostChimeClip => _frostChimeClip;
        public static AudioClip CrashClip => _crashClip;
        public static AudioClip JumpPopClip => _jumpPopClip;

        public static Mesh SkiMesh => _skiMesh;
        public static Mesh SkiRuneMesh => _skiRuneMesh;
        public static Mesh PoleMesh => _poleMesh;
        public static Mesh SnowboardMesh => _snowboardMesh;
        public static Mesh SnowboardRuneMesh => _snowboardRuneMesh;
        public static Mesh RampDeckMesh => _rampDeckMesh;
        public static Mesh RampRailMesh => _rampRailMesh;

        public static Material CreateGateBannerMaterial(Color baseColor, Color emissionColor)
        {
            Material mat = new Material(FrostRuneMat);
            mat.name = "UllrGateBannerMat";
            if (!IsHeadless)
            {
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emissionColor);
                }
            }
            return mat;
        }

        private static void HarvestMaterialsAndAudio()
        {
            Shader baseShader = FindSafeShader();

            _darkWoodMat = CloneMaterialFromPrefab(new[] { "ShieldWood", "Club", "wood_beam" }, "UllrDarkWood", baseShader,
                new Color(0.25f, 0.16f, 0.10f), 0.05f, 0.35f, Color.black);

            _leatherMat = CloneMaterialFromPrefab(new[] { "ArmorLeatherChest", "HelmetLeather", "ShieldWood" }, "UllrLeather", baseShader,
                new Color(0.36f, 0.21f, 0.11f), 0.05f, 0.25f, Color.black);

            _silverEdgeMat = CloneMaterialFromPrefab(new[] { "Silver", "SwordSilver", "Frostner", "Iron" }, "UllrSilverEdge", baseShader,
                new Color(0.80f, 0.89f, 0.97f), 0.82f, 0.80f, new Color(0.06f, 0.18f, 0.28f));

            _frostRuneMat = CloneMaterialFromPrefab(new[] { "Crystal", "DragonTear", "Frostner" }, "UllrFrostRune", baseShader,
                new Color(0.38f, 0.94f, 1.0f), 0.15f, 0.92f, new Color(0.25f, 0.95f, 1.0f) * 2.5f);

            _snowDeckMat = CloneMaterialFromPrefab(new[] { "Snowball", "FreezeGland", "Stone" }, "UllrSnowDeck", baseShader,
                new Color(0.86f, 0.94f, 1.0f), 0.05f, 0.68f, new Color(0.06f, 0.14f, 0.22f));

            Shader particleShader = Shader.Find("Particles/Standard Unlit")
                                    ?? Shader.Find("Legacy Shaders/Particles/Additive")
                                    ?? Shader.Find("Sprites/Default")
                                    ?? baseShader;

            _trailRibbonMat = new Material(particleShader) { name = "UllrFrostTrail" };
            _snowParticleMat = new Material(particleShader) { name = "UllrSnowSpray" };

            if (!IsHeadless)
            {
                Texture2D softTex = CreateSoftRadialTexture(32, new Color(0.75f, 0.95f, 1f, 0.9f));
                if (_trailRibbonMat.HasProperty("_MainTex")) _trailRibbonMat.SetTexture("_MainTex", softTex);
                if (_trailRibbonMat.HasProperty("_Color")) _trailRibbonMat.SetColor("_Color", new Color(0.45f, 0.92f, 1f, 0.65f));

                Texture2D flakeTex = CreateSoftRadialTexture(32, new Color(0.95f, 0.98f, 1f, 0.95f));
                if (_snowParticleMat.HasProperty("_MainTex")) _snowParticleMat.SetTexture("_MainTex", flakeTex);
                if (_snowParticleMat.HasProperty("_Color")) _snowParticleMat.SetColor("_Color", new Color(0.92f, 0.97f, 1f, 0.85f));

                HarvestAudioClips();
            }
        }

        private static Shader FindSafeShader()
        {
            string[] candidates =
            {
                "Custom/Piece",
                "Standard",
                "Universal Render Pipeline/Lit",
                "Legacy Shaders/Diffuse",
                "Sprites/Default"
            };
            foreach (string name in candidates)
            {
                Shader s = Shader.Find(name);
                if (s != null) return s;
            }
            GameObject wood = PrefabManager.Instance?.GetPrefab("Wood");
            if (wood != null)
            {
                Renderer r = wood.GetComponentInChildren<Renderer>(true);
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null)
                    return r.sharedMaterial.shader;
            }
            return Shader.Find("Hidden/InternalErrorShader");
        }

        private static Material CloneMaterialFromPrefab(string[] prefabNames, string matName, Shader fallbackShader,
            Color color, float metallic, float glossiness, Color emission)
        {
            Material result = null;
            if (PrefabManager.Instance != null)
            {
                foreach (string pName in prefabNames)
                {
                    GameObject go = PrefabManager.Instance.GetPrefab(pName);
                    if (go == null) continue;
                    MeshRenderer mr = go.GetComponentInChildren<MeshRenderer>(true);
                    if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.shader != null)
                    {
                        result = new Material(mr.sharedMaterial);
                        break;
                    }
                }
            }
            if (result == null)
            {
                result = CreateFallbackMaterial(color, metallic, glossiness, emission, fallbackShader);
            }
            result.name = matName;
            if (!IsHeadless)
            {
                // Clear inherited albedo textures when we want clean tinted/emissive surfaces on runes/edges
                if (emission.maxColorComponent > 0.2f && result.HasProperty("_MainTex"))
                {
                    result.SetTexture("_MainTex", Texture2D.whiteTexture);
                }
                if (result.HasProperty("_Color")) result.SetColor("_Color", color);
                if (result.HasProperty("_Metallic")) result.SetFloat("_Metallic", metallic);
                if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", glossiness);
                if (emission.maxColorComponent > 0.001f && result.HasProperty("_EmissionColor"))
                {
                    result.EnableKeyword("_EMISSION");
                    result.SetColor("_EmissionColor", emission);
                }
            }
            return result;
        }

        private static Material CreateFallbackMaterial(Color color, float metallic, float glossiness, Color emission, Shader shader = null)
        {
            Shader s = shader ?? FindSafeShader();
            Material m = new Material(s);
            if (!IsHeadless)
            {
                if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", glossiness);
                if (emission.maxColorComponent > 0.001f && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission);
                }
            }
            return m;
        }

        private static void HarvestAudioClips()
        {
            try
            {
                AudioClip[] clips = Resources.FindObjectsOfTypeAll<AudioClip>();
                foreach (AudioClip clip in clips)
                {
                    if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                    string lower = clip.name.ToLowerInvariant();
                    if (_snowStepClip == null && lower.Contains("footstep") && lower.Contains("snow"))
                        _snowStepClip = clip;
                    else if (_frostChimeClip == null && (lower.Contains("frost") || lower.Contains("crystal") || lower.Contains("ice")) && !lower.Contains("loop"))
                        _frostChimeClip = clip;
                    else if (_crashClip == null && (lower.Contains("snow") || lower.Contains("body") || lower.Contains("wood_break")))
                        _crashClip = clip;
                    else if (_jumpPopClip == null && lower.Contains("jump"))
                        _jumpPopClip = clip;
                }
            }
            catch (Exception ex)
            {
                UllrPlugin.Log?.LogWarning($"Audio harvest warning: {ex.Message}");
            }
        }

        private static Texture2D CreateSoftRadialTexture(int size, Color tint)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float half = (size - 1) * 0.5f;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a;
                    pixels[y * size + x] = new Color(tint.r, tint.g, tint.b, tint.a * a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static void BuildMeshes()
        {
            _skiMesh = BuildSkiBodyMesh();
            _skiRuneMesh = BuildRibbonRuneStripMesh(length: 1.50f, width: 0.036f, yOffset: 0.024f, tipCurlStart: 0.62f, tipHeight: 0.14f);
            _poleMesh = BuildSkiPoleMesh();
            _snowboardMesh = BuildSnowboardBodyMesh();
            _snowboardRuneMesh = BuildRibbonRuneStripMesh(length: 1.34f, width: 0.085f, yOffset: 0.026f, tipCurlStart: 0.54f, tipHeight: 0.10f);
            _rampDeckMesh = BuildJumpRampDeckMesh();
            _rampRailMesh = BuildJumpRampRailsMesh();
        }

        private static Mesh BuildSkiBodyMesh()
        {
            // Sculpted 1.92m twin-tip Norse ski along Z axis (-0.90m tail to +1.02m dragon-prow tip)
            int segments = 20;
            float zMin = -0.90f;
            float zMax = 1.02f;
            float thickness = 0.020f;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float z = Mathf.Lerp(zMin, zMax, t);

                // Hourglass carving sidecut + tapered dragon tip & twin tail
                float halfWidth;
                if (t > 0.82f)
                {
                    float tipT = (t - 0.82f) / 0.18f;
                    halfWidth = Mathf.Lerp(0.058f, 0.012f, tipT * tipT);
                }
                else if (t < 0.12f)
                {
                    float tailT = (0.12f - t) / 0.12f;
                    halfWidth = Mathf.Lerp(0.052f, 0.028f, tailT * tailT);
                }
                else
                {
                    float mid = Mathf.Sin((t - 0.12f) / 0.70f * Mathf.PI);
                    halfWidth = Mathf.Lerp(0.055f, 0.043f, mid); // carved waist
                }

                // Camber underfoot + upturned prow tip & twin tail
                float yBase = 0f;
                if (t > 0.76f)
                {
                    float u = (t - 0.76f) / 0.24f;
                    yBase = u * u * 0.17f;
                }
                else if (t < 0.14f)
                {
                    float u = (0.14f - t) / 0.14f;
                    yBase = u * u * 0.075f;
                }
                else
                {
                    float c = Mathf.Sin((t - 0.14f) / 0.62f * Mathf.PI);
                    yBase = c * 0.008f;
                }

                float localThick = thickness * (t > 0.85f ? Mathf.Lerp(1f, 0.55f, (t - 0.85f) / 0.15f) : 1f);

                // 4 vertices per cross-section: 0=bottom-left, 1=bottom-right, 2=top-right, 3=top-left
                verts.Add(new Vector3(-halfWidth, yBase, z));
                verts.Add(new Vector3(halfWidth, yBase, z));
                verts.Add(new Vector3(halfWidth * 0.92f, yBase + localThick, z));
                verts.Add(new Vector3(-halfWidth * 0.92f, yBase + localThick, z));

                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
                uvs.Add(new Vector2(1f, t));
                uvs.Add(new Vector2(0f, t));
            }

            StitchLoftedTube(verts, tris, segments);
            Mesh m = new Mesh { name = "UllrMythicSkiMesh" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildSnowboardBodyMesh()
        {
            // Sculpted 1.66m x 0.30m Moder's Drakkar twin-tip snowboard along Z axis
            int segments = 22;
            float zMin = -0.83f;
            float zMax = 0.83f;
            float thickness = 0.022f;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float z = Mathf.Lerp(zMin, zMax, t);
                float sym = Mathf.Abs(t - 0.5f) * 2f; // 0 at center, 1 at tips

                float halfWidth;
                if (sym > 0.74f)
                {
                    float tipU = (sym - 0.74f) / 0.26f;
                    halfWidth = Mathf.Lerp(0.152f, 0.055f, tipU * tipU);
                }
                else
                {
                    float waist = 1f - (sym / 0.74f);
                    halfWidth = Mathf.Lerp(0.152f, 0.132f, waist);
                }

                float yBase = 0f;
                if (sym > 0.68f)
                {
                    float u = (sym - 0.68f) / 0.32f;
                    yBase = u * u * 0.115f;
                }

                verts.Add(new Vector3(-halfWidth, yBase, z));
                verts.Add(new Vector3(halfWidth, yBase, z));
                verts.Add(new Vector3(halfWidth * 0.95f, yBase + thickness, z));
                verts.Add(new Vector3(-halfWidth * 0.95f, yBase + thickness, z));

                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
                uvs.Add(new Vector2(1f, t));
                uvs.Add(new Vector2(0f, t));
            }

            StitchLoftedTube(verts, tris, segments);
            Mesh m = new Mesh { name = "ModerDrakkarBoardMesh" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildRibbonRuneStripMesh(float length, float width, float yOffset, float tipCurlStart, float tipHeight)
        {
            int segments = 18;
            float halfLen = length * 0.5f;
            float halfW = width * 0.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float z = Mathf.Lerp(-halfLen, halfLen, t);
                float absZ = Mathf.Abs(z);
                float y = yOffset;
                if (absZ > tipCurlStart)
                {
                    float u = (absZ - tipCurlStart) / Mathf.Max(0.05f, halfLen - tipCurlStart);
                    y += u * u * tipHeight;
                }
                // Diamond-stepped Norse rune pattern width modulation
                float runePulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 7f));
                float w = halfW * runePulse * (1f - 0.45f * Mathf.Pow(Mathf.Abs(t - 0.5f) * 2f, 3f));

                verts.Add(new Vector3(-w, y, z));
                verts.Add(new Vector3(w, y, z));
                uvs.Add(new Vector2(0f, t * 6f));
                uvs.Add(new Vector2(1f, t * 6f));

                if (i < segments)
                {
                    int b = i * 2;
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            Mesh m = new Mesh { name = "UllrRuneInlayMesh" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildSkiPoleMesh()
        {
            // Pole extending from grip at (0,0,0) down/back along -Y to -1.20m
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            AddCylinder(verts, tris, uvs, new Vector3(0f, 0.08f, 0f), new Vector3(0f, -1.18f, 0f), 0.013f, 0.008f, 6);
            // Snow basket disk at y = -1.04m
            AddCylinder(verts, tris, uvs, new Vector3(0f, -1.03f, 0f), new Vector3(0f, -1.055f, 0f), 0.055f, 0.055f, 8);
            // Leather handle grip
            AddCylinder(verts, tris, uvs, new Vector3(0f, 0.12f, 0f), new Vector3(0f, -0.06f, 0f), 0.020f, 0.018f, 6);

            Mesh m = new Mesh { name = "SkadiSkiPoleMesh" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildJumpRampDeckMesh()
        {
            // Smooth curved kicker ramp: width 3.6m, length 5.4m (-2.7m entry to +2.7m lip), lip height 1.75m
            // Entry skirts dip -0.28m underground so skis glide seamlessly onto the ramp even on uneven snow!
            int segZ = 14;
            float halfW = 1.80f;
            float zStart = -2.70f;
            float zEnd = 2.70f;
            float baseFloor = -0.35f;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            for (int i = 0; i <= segZ; i++)
            {
                float t = i / (float)segZ;
                float z = Mathf.Lerp(zStart, zEnd, t);
                // Smooth curve: slightly below 0 at t=0, tangent to 0 around t=0.12, curving smoothly up to 1.75m at lip (approx 30 deg launch!)
                float u = Mathf.Clamp01((t - 0.08f) / 0.92f);
                float yTop = (t < 0.08f)
                    ? Mathf.Lerp(-0.18f, 0.0f, t / 0.08f)
                    : (0.28f * u + 1.47f * u * u);

                verts.Add(new Vector3(-halfW, baseFloor, z));
                verts.Add(new Vector3(halfW, baseFloor, z));
                verts.Add(new Vector3(halfW, yTop, z));
                verts.Add(new Vector3(-halfW, yTop, z));

                uvs.Add(new Vector2(0f, t * 2f));
                uvs.Add(new Vector2(1f, t * 2f));
                uvs.Add(new Vector2(1f, t * 2f));
                uvs.Add(new Vector2(0f, t * 2f));
            }

            StitchLoftedTube(verts, tris, segZ);
            Mesh m = new Mesh { name = "UllrJumpRampDeck" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildJumpRampRailsMesh()
        {
            // Raised glowing rune side-curbs along both left & right edges of the kicker ramp
            int segZ = 14;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            float[] sides = { -1.82f, 1.82f };
            for (int s = 0; s < sides.Length; s++)
            {
                int baseVertexOffset = verts.Count;
                float cx = sides[s];
                float hw = 0.06f;
                for (int i = 0; i <= segZ; i++)
                {
                    float t = i / (float)segZ;
                    float z = Mathf.Lerp(-2.50f, 2.70f, t);
                    float u = Mathf.Clamp01((t - 0.08f) / 0.92f);
                    float y = (t < 0.08f ? 0f : (0.28f * u + 1.47f * u * u)) + 0.02f;
                    float h = 0.09f;

                    verts.Add(new Vector3(cx - hw, y, z));
                    verts.Add(new Vector3(cx + hw, y, z));
                    verts.Add(new Vector3(cx + hw, y + h, z));
                    verts.Add(new Vector3(cx - hw, y + h, z));
                    uvs.Add(new Vector2(0f, t));
                    uvs.Add(new Vector2(1f, t));
                    uvs.Add(new Vector2(1f, t));
                    uvs.Add(new Vector2(0f, t));
                }
                StitchLoftedTubeOffset(verts, tris, segZ, baseVertexOffset);
            }

            Mesh m = new Mesh { name = "UllrJumpRampRails" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void StitchLoftedTube(List<Vector3> verts, List<int> tris, int segments)
        {
            StitchLoftedTubeOffset(verts, tris, segments, 0);
        }

        private static void StitchLoftedTubeOffset(List<Vector3> verts, List<int> tris, int segments, int vOffset)
        {
            for (int i = 0; i < segments; i++)
            {
                int a = vOffset + i * 4;
                int b = vOffset + (i + 1) * 4;
                // Bottom (0-1)
                AddQuad(tris, a + 0, a + 1, b + 1, b + 0);
                // Right side (1-2)
                AddQuad(tris, a + 1, a + 2, b + 2, b + 1);
                // Top (2-3)
                AddQuad(tris, a + 3, b + 3, b + 2, a + 2);
                // Left side (3-0)
                AddQuad(tris, a + 0, b + 0, b + 3, a + 3);
            }
            // Back cap
            AddQuad(tris, vOffset + 0, vOffset + 3, vOffset + 2, vOffset + 1);
            // Front cap
            int f = vOffset + segments * 4;
            AddQuad(tris, f + 0, f + 1, f + 2, f + 3);
        }

        private static void AddCylinder(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
            Vector3 p0, Vector3 p1, float r0, float r1, int sides)
        {
            int start = verts.Count;
            Vector3 axis = (p1 - p0).normalized;
            Vector3 perp1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 perp2 = Vector3.Cross(axis, perp1).normalized;

            for (int ring = 0; ring <= 1; ring++)
            {
                Vector3 center = ring == 0 ? p0 : p1;
                float r = ring == 0 ? r0 : r1;
                for (int s = 0; s <= sides; s++)
                {
                    float ang = (s / (float)sides) * Mathf.PI * 2f;
                    Vector3 offset = (perp1 * Mathf.Cos(ang) + perp2 * Mathf.Sin(ang)) * r;
                    verts.Add(center + offset);
                    uvs.Add(new Vector2(s / (float)sides, ring));
                }
            }

            int stride = sides + 1;
            for (int s = 0; s < sides; s++)
            {
                int a0 = start + s;
                int a1 = start + s + 1;
                int b0 = start + stride + s;
                int b1 = start + stride + s + 1;
                AddQuad(tris, a0, b0, b1, a1);
            }
        }

        private static void AddQuad(List<int> tris, int v0, int v1, int v2, int v3)
        {
            tris.Add(v0); tris.Add(v1); tris.Add(v2);
            tris.Add(v0); tris.Add(v2); tris.Add(v3);
        }

        private static void BuildIcons()
        {
            SkisIcon = CreateIconSprite("UllrSkisIcon", DrawSkisIcon);
            SnowboardIcon = CreateIconSprite("ModerSnowboardIcon", DrawSnowboardIcon);
            FrostWardIcon = CreateIconSprite("UllrFrostWardIcon", DrawFrostWardIcon);
            RampIcon = CreateIconSprite("UllrRampIcon", DrawRampIcon);
            GateIcon = CreateIconSprite("UllrGateIcon", DrawGateIcon);
            RopeTowIcon = CreateIconSprite("UllrRopeTowIcon", DrawRopeTowIcon);
        }

        private static Sprite CreateIconSprite(string name, Action<Color[], int> painter)
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] px = new Color[size * size];

            // Subtle dark Nordic badge backdrop
            Color bgInner = new Color(0.08f, 0.14f, 0.22f, 0.92f);
            Color border = new Color(0.32f, 0.82f, 0.98f, 0.95f);
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist > 0.96f)
                        px[y * size + x] = Color.clear;
                    else if (dist > 0.88f)
                        px[y * size + x] = border;
                    else
                        px[y * size + x] = Color.Lerp(bgInner, new Color(0.03f, 0.06f, 0.10f, 0.92f), dist);
                }
            }

            painter(px, size);
            tex.SetPixels(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        }

        private static void DrawLine(Color[] px, int size, Vector2 a, Vector2 b, float radius, Color col)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1));
            Vector2 ab = b - a;
            float lenSq = Mathf.Max(0.0001f, ab.sqrMagnitude);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
                    float d = Vector2.Distance(p, a + ab * t);
                    if (d <= radius + 0.8f)
                    {
                        float alpha = Mathf.Clamp01(radius + 0.6f - d) * col.a;
                        BlendPixel(px, size, x, y, new Color(col.r, col.g, col.b, alpha));
                    }
                }
            }
        }

        private static void BlendPixel(Color[] px, int size, int x, int y, Color src)
        {
            Color dst = px[y * size + x];
            float outA = src.a + dst.a * (1f - src.a);
            if (outA <= 0.001f) return;
            Color rgb = (src * src.a + dst * dst.a * (1f - src.a)) / outA;
            rgb.a = outA;
            px[y * size + x] = rgb;
        }

        private static void DrawSkisIcon(Color[] px, int s)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            Color cyan = new Color(0.35f, 0.95f, 1.0f, 1f);
            Color silver = new Color(0.86f, 0.93f, 1.0f, 1f);
            // Crossed twin Nordic skis + poles
            DrawLine(px, s, new Vector2(14, 14), new Vector2(46, 50), 2.8f, wood);
            DrawLine(px, s, new Vector2(14, 14), new Vector2(46, 50), 1.1f, cyan);
            DrawLine(px, s, new Vector2(20, 11), new Vector2(52, 47), 2.8f, wood);
            DrawLine(px, s, new Vector2(20, 11), new Vector2(52, 47), 1.1f, cyan);
            // Crossed ski poles
            DrawLine(px, s, new Vector2(49, 13), new Vector2(15, 49), 1.3f, silver);
            DrawLine(px, s, new Vector2(53, 17), new Vector2(19, 53), 1.3f, silver);
        }

        private static void DrawSnowboardIcon(Color[] px, int s)
        {
            Color board = new Color(0.24f, 0.34f, 0.48f, 1f);
            Color edge = new Color(0.84f, 0.93f, 1.0f, 1f);
            Color rune = new Color(0.30f, 0.96f, 1.0f, 1f);
            DrawLine(px, s, new Vector2(15, 15), new Vector2(49, 49), 6.5f, edge);
            DrawLine(px, s, new Vector2(16, 16), new Vector2(48, 48), 5.0f, board);
            DrawLine(px, s, new Vector2(18, 18), new Vector2(46, 46), 1.6f, rune);
            // Bindings
            DrawLine(px, s, new Vector2(23, 29), new Vector2(29, 23), 2.0f, edge);
            DrawLine(px, s, new Vector2(35, 41), new Vector2(41, 35), 2.0f, edge);
        }

        private static void DrawFrostWardIcon(Color[] px, int s)
        {
            Color ice = new Color(0.42f, 0.95f, 1.0f, 1f);
            Color core = new Color(0.90f, 0.99f, 1.0f, 1f);
            Vector2 c = new Vector2(32, 32);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 tip = c + dir * 20f;
                DrawLine(px, s, c, tip, 1.8f, ice);
                Vector2 branchBase = c + dir * 12f;
                Vector2 b1 = new Vector2(Mathf.Cos(a + 0.55f), Mathf.Sin(a + 0.55f)) * 6f;
                Vector2 b2 = new Vector2(Mathf.Cos(a - 0.55f), Mathf.Sin(a - 0.55f)) * 6f;
                DrawLine(px, s, branchBase, branchBase + b1, 1.3f, core);
                DrawLine(px, s, branchBase, branchBase + b2, 1.3f, core);
            }
        }

        private static void DrawRampIcon(Color[] px, int s)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            Color snow = new Color(0.88f, 0.96f, 1.0f, 1f);
            Color rune = new Color(0.32f, 0.95f, 1.0f, 1f);
            DrawLine(px, s, new Vector2(12, 18), new Vector2(50, 18), 2.5f, wood);
            DrawLine(px, s, new Vector2(50, 18), new Vector2(50, 44), 2.5f, wood);
            DrawLine(px, s, new Vector2(12, 18), new Vector2(34, 24), 3.0f, snow);
            DrawLine(px, s, new Vector2(34, 24), new Vector2(50, 44), 3.0f, snow);
            DrawLine(px, s, new Vector2(14, 21), new Vector2(49, 46), 1.3f, rune);
        }

        private static void DrawGateIcon(Color[] px, int s)
        {
            Color pole = new Color(0.82f, 0.90f, 0.98f, 1f);
            Color banner = new Color(0.25f, 0.94f, 0.88f, 1f);
            DrawLine(px, s, new Vector2(18, 13), new Vector2(18, 51), 2.4f, pole);
            DrawLine(px, s, new Vector2(46, 13), new Vector2(46, 51), 2.4f, pole);
            DrawLine(px, s, new Vector2(18, 43), new Vector2(46, 43), 4.2f, banner);
        }

        private static void DrawRopeTowIcon(Color[] px, int s)
        {
            Color wood = new Color(0.62f, 0.42f, 0.22f, 1f);
            Color rope = new Color(0.36f, 0.96f, 1.0f, 1f);
            DrawLine(px, s, new Vector2(14, 14), new Vector2(14, 34), 2.3f, wood);
            DrawLine(px, s, new Vector2(50, 28), new Vector2(50, 50), 2.3f, wood);
            DrawLine(px, s, new Vector2(14, 32), new Vector2(50, 48), 2.0f, rope);
            DrawLine(px, s, new Vector2(28, 38), new Vector2(28, 25), 1.5f, rope);
        }
    }
}
