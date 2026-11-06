using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あおだる必殺技「六腕」専用カットイン。
    // 既存のSpecialCutIn/IwadaruCutInと同じく、別ステージ＋RenderTexture＋Time.timeScale=0で再生する。
    // 顔の周囲に六本のエネルギー腕と、実際のハンマーモデルを複製した六本のハンマーを旋回させる。
    public class AodaruCutIn : MonoBehaviour
    {
        const float Duration = 1.35f;
        const int CutInLayer = 30;

        static AodaruCutIn s_instance;
        static Font s_font;
        static Texture2D s_backdropTexture;

        string m_specialName;
        Color m_accent = new Color(0.22f, 0.58f, 1.0f, 1.0f);
        float m_start;
        bool m_active;
        float m_previousTimeScale = 1.0f;

        GameObject m_stage;
        Camera m_camera;
        RenderTexture m_renderTexture;
        Transform m_subject;
        GameObject m_backdrop;
        Material m_backdropMaterial;
        Material m_armMaterial;
        Material m_trailMaterial;
        Material m_spokeMaterial;
        Material m_hammerSilhouetteMaterial;
        Material m_concentrationMaterial;
        Material m_energyHammerMaterial;
        Vector3 m_cameraBasePosition;
        bool m_impactDone;
        float m_impactPulse;
        float m_impactFlashUntil;

        // 六腕を表す背景ホイール。白い6本の筋とオレンジのハンマー影絵を回す。
        Transform m_spokeWheel;
        readonly List<Transform> m_spokes = new List<Transform>();
        readonly List<Mesh> m_backgroundMeshes = new List<Mesh>();

        [Header("背景")]
        [SerializeField, Range(4, 8)] int m_spokeCount = 6;
        [SerializeField, Min(0.0f)] float m_spokeRotationSpeed = 72.0f;
        [SerializeField, Min(0.01f)] float m_spokeWidth = 0.18f;
        [SerializeField, Min(0.0f)] float m_spokeAppearStagger = 0.065f;
        [SerializeField, Min(0.05f)] float m_spokeAppearTime = 0.18f;
        [SerializeField, Min(0.05f)] float m_spokeOutroTime = 0.22f;
        [SerializeField, Range(4, 16)] int m_concentrationLineCount = 9;
        [SerializeField, Min(0.005f)] float m_concentrationLineWidth = 0.03f;
        [SerializeField] Color m_spokeColor = new Color(1.0f, 1.0f, 1.0f, 0.55f);
        [SerializeField] Color m_hammerSilhouetteColor = new Color(0.62f, 0.30f, 0.10f, 0.95f);

        [Header("腕")]
        [SerializeField, Min(0.0f)] float m_armAppearStagger = 0.075f;
        [SerializeField, Min(0.05f)] float m_armAppearTime = 0.24f;
        [SerializeField, Min(0.0f)] float m_armsImpactTime = 0.72f;

        [Header("タイトル")]
        [SerializeField] float m_titleTilt = -4.0f;
        [SerializeField] float m_titleIntroScale = 1.4f;
        [SerializeField, Range(0.55f, 0.78f)] float m_titleY = 0.66f;
        [SerializeField] Color m_titleAccent = new Color(1.0f, 0.45f, 0.10f, 1.0f);

        readonly List<GameObject> m_createdObjects = new List<GameObject>();
        readonly List<Arm> m_arms = new List<Arm>();

        GUIStyle m_titleStyle;
        bool m_stylesReady;

        struct Arm
        {
            public Transform Hand;
            public Transform Cylinder;
            public float BaseAngle;
            public float Radius;
            public float Speed;
            public float Height;
            public float Phase;
            public float Tilt;
            public float AppearDelay;
            public float Side;
            public Vector3 HandBase;
            public float OrbitSpeed;
            public float OrbitRadius;
        }

        public static void Show(string specialName, DarumaCharacter character)
        {
            if (s_instance == null)
            {
                var go = new GameObject("AodaruCutIn");
                s_instance = go.AddComponent<AodaruCutIn>();
            }
            s_instance.Play(specialName, character);
        }

        public static bool IsPlaying => s_instance != null && s_instance.m_active;

        private void Awake()
        {
            s_instance = this;
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            End();
            if (s_instance == this) s_instance = null;
        }

        void Play(string specialName, DarumaCharacter character)
        {
            if (!m_active)
            {
                m_previousTimeScale = Time.timeScale;
                Time.timeScale = 0.0f;
            }
            else
            {
                CleanupStage();
            }

            m_specialName = specialName;
            m_start = Time.unscaledTime;
            m_active = true;
            m_impactDone = false;
            m_impactPulse = 0.0f;
            m_impactFlashUntil = 0.0f;

            BuildStage(character);
        }

        private void Update()
        {
            if (!m_active) return;
            float age = Time.unscaledTime - m_start;
            if (age > Duration)
            {
                End();
                return;
            }
            Animate(age);
        }

        void End()
        {
            if (!m_active) return;
            m_active = false;
            CleanupStage();
            Time.timeScale = m_previousTimeScale;
        }

        void BuildStage(DarumaCharacter character)
        {
            m_stage = new GameObject("AodaruCutInStage");
            m_stage.transform.position = new Vector3(10000.0f, 10000.0f, 10000.0f);

            m_renderTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            m_renderTexture.Create();

            var cameraObject = new GameObject("AodaruCutInCamera");
            cameraObject.transform.SetParent(m_stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.0f, 1.72f, -3.10f);
            m_cameraBasePosition = cameraObject.transform.localPosition;
            m_camera = cameraObject.AddComponent<Camera>();
            m_camera.orthographic = false;
            m_camera.fieldOfView = 52.0f;
            m_camera.clearFlags = CameraClearFlags.SolidColor;
            m_camera.backgroundColor = new Color(0.040f, 0.100f, 0.220f, 1.0f);
            m_camera.cullingMask = 1 << CutInLayer;
            m_camera.nearClipPlane = 0.05f;
            m_camera.farClipPlane = 40.0f;
            m_camera.targetTexture = m_renderTexture;
            m_camera.enabled = true;
            cameraObject.transform.LookAt(m_stage.transform.TransformPoint(new Vector3(0.0f, 1.62f, 0.0f)));

            CreateLight("AodaruCutInKey", new Vector3(2.5f, 3.6f, -2.0f),
                Quaternion.Euler(38.0f, -28.0f, 0.0f), new Color(0.72f, 0.88f, 1.0f), 2.2f);
            CreateLight("AodaruCutInRim", new Vector3(-2.3f, 2.0f, -1.5f),
                Quaternion.identity, new Color(0.18f, 0.52f, 1.0f), 9.0f);

            CreateBackdrop();
            CreateMaterials();

            if (character != null)
            {
                Transform headSource = FindPart(character.transform, "Head");
                if (headSource != null)
                {
                    var head = CloneVisual(headSource.gameObject, m_stage.transform, "CutInAodaruHead");
                    m_subject = head.transform;
                    m_subject.localPosition = new Vector3(0.0f, 1.58f, 0.0f);
                    m_subject.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                    m_subject.localScale = Vector3.one * 2.15f;
                    m_createdObjects.Add(head);
                }

                CreateArms(character);
                CreateSpokeBackground();
            }
        }

        void CreateMaterials()
        {
            // 3Dの腕は水色寄りの白にして、背景の白い筋と同化しすぎないようにする
            m_armMaterial = CreateUnlitMaterial(new Color(0.72f, 0.90f, 1.0f, 0.55f), true);
            m_trailMaterial = CreateUnlitMaterial(new Color(0.82f, 0.95f, 1.0f, 0.8f), true);
            m_spokeMaterial = CreateUnlitMaterial(m_spokeColor, true);
            m_hammerSilhouetteMaterial = CreateUnlitMaterial(m_hammerSilhouetteColor, false);
            m_concentrationMaterial = CreateUnlitMaterial(new Color(1.0f, 1.0f, 1.0f, 0.30f), true);
            // 通常の木製ハンマーではなく、必殺技用の青いエネルギー体にする
            m_energyHammerMaterial = CreateUnlitMaterial(new Color(0.40f, 0.92f, 1.65f, 1.0f), true);
        }

        Material CreateUnlitMaterial(Color color, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (additive)
            {
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1.0f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2.0f);
                if (material.HasProperty("_SrcBlend"))
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0.0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return material;
        }

        void CreateArms(DarumaCharacter character)
        {
            Transform hammerSource = character.Hammer != null ? character.Hammer.transform : null;
            float[] baseAngles = { -90.0f, -30.0f, 30.0f, 90.0f, 150.0f, 210.0f };
            float[] radii = { 1.95f, 2.20f, 1.82f, 2.05f, 2.25f, 1.88f };
            float[] heights = { 1.35f, 1.92f, 2.30f, 1.45f, 1.05f, 1.78f };
            float[] speeds = { 72.0f, 58.0f, 66.0f, 51.0f, 63.0f, 55.0f };
            float[] sides = { -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f };
            // 6本が重ならない固定スロット。左右3本ずつ、高さを大きく変える。
            Vector3[] handSlots =
            {
                new Vector3(-1.52f, 2.30f, -0.12f),
                new Vector3(-1.98f, 1.60f, -0.12f),
                new Vector3(-1.62f, 0.92f, -0.12f),
                new Vector3(1.58f, 2.32f, -0.12f),
                new Vector3(2.02f, 1.55f, -0.12f),
                new Vector3(1.66f, 0.88f, -0.12f),
            };
            float[] orbitSpeeds = { 1.7f, -1.5f, 1.9f, -1.8f, 1.6f, -2.0f };
            float[] orbitRadii = { 0.07f, 0.09f, 0.06f, 0.08f, 0.07f, 0.06f };

            for (int i = 0; i < 6; i++)
            {
                var hand = new GameObject($"AodaruCutInHand{i}").transform;
                hand.SetParent(m_stage.transform, false);
                hand.gameObject.layer = CutInLayer;

                if (hammerSource != null)
                {
                    var hammer = CreateHammerVisual(hammerSource, hand);
                    hammer.transform.localPosition = Vector3.zero;
                    hammer.transform.localRotation = Quaternion.identity;
                    hammer.transform.localScale = Vector3.one * 0.62f;
                    // 木製の茶色を消し、青いエネルギー体のハンマーにする
                    foreach (var hammerRenderer in hammer.GetComponentsInChildren<Renderer>(true))
                    {
                        if (hammerRenderer == null) continue;
                        if (m_energyHammerMaterial != null)
                            hammerRenderer.sharedMaterial = m_energyHammerMaterial;
                        hammerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        hammerRenderer.receiveShadows = false;
                    }
                    m_createdObjects.Add(hammer.gameObject);
                }

                var trailObject = new GameObject("HammerTrail");
                trailObject.transform.SetParent(hand, false);
                trailObject.layer = CutInLayer;
                trailObject.transform.localPosition = new Vector3(0.0f, 0.0f, 0.6f);
                var trail = trailObject.AddComponent<TrailRenderer>();
                if (m_trailMaterial != null) trail.sharedMaterial = m_trailMaterial;
                trail.time = 0.42f;
                trail.minVertexDistance = 0.035f;
                trail.autodestruct = false;
                trail.emitting = true;
                trail.alignment = LineAlignment.View;
                trail.numCapVertices = 2;
                trail.numCornerVertices = 2;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.startWidth = 0.16f;
                trail.endWidth = 0.0f;
                trail.startColor = new Color(0.75f, 0.95f, 1.0f, 0.9f);
                trail.endColor = new Color(0.10f, 0.40f, 1.0f, 0.0f);

                var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cylinder.name = $"AodaruEnergyArm{i}";
                var collider = cylinder.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                cylinder.transform.SetParent(m_stage.transform, false);
                cylinder.layer = CutInLayer;
                var renderer = cylinder.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    if (m_armMaterial != null) renderer.sharedMaterial = m_armMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                m_arms.Add(new Arm
                {
                    Hand = hand,
                    Cylinder = cylinder.transform,
                    BaseAngle = baseAngles[i] + Random.Range(-8.0f, 8.0f),
                    Radius = radii[i],
                    Speed = speeds[i],
                    Height = heights[i],
                    Phase = Random.Range(0.0f, 6.28f),
                    Tilt = Random.Range(-22.0f, 22.0f),
                    AppearDelay = i * m_armAppearStagger,
                    Side = sides[i],
                    HandBase = handSlots[i],
                    OrbitSpeed = orbitSpeeds[i],
                    OrbitRadius = orbitRadii[i],
                });

                // 最初のAnimateまで胴体の中に隠す
                hand.localScale = Vector3.zero;
                cylinder.transform.localScale = Vector3.zero;
            }
        }

        void CreateBackdrop()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "AodaruCutInBackdrop";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            quad.transform.SetParent(m_stage.transform, false);
            quad.layer = CutInLayer;
            quad.transform.position = m_camera.transform.position + m_camera.transform.forward * 12.0f;
            quad.transform.rotation = m_camera.transform.rotation;
            quad.transform.localScale = new Vector3(20.0f, 12.0f, 1.0f);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                m_backdropMaterial = new Material(shader);
                m_backdropMaterial.mainTexture = GetBackdropTexture();
                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = m_backdropMaterial;
            }
            m_backdrop = quad;
            m_createdObjects.Add(quad);
        }

        void CreateLight(string name, Vector3 position, Quaternion rotation, Color color, float intensity)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(m_stage.transform, false);
            lightObject.transform.localPosition = position;
            lightObject.transform.localRotation = rotation;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << CutInLayer;
        }

        // 六腕テーマの背景。白い6本の筋とオレンジのハンマー影絵を、3Dハンマーと同じ向きに回す。
        void CreateSpokeBackground()
        {
            m_spokeWheel = new GameObject("AodaruSpokeWheel").transform;
            m_spokeWheel.SetParent(m_stage.transform, false);
            m_spokeWheel.localPosition = new Vector3(0.0f, 1.58f, 0.55f);

            int count = Mathf.Clamp(m_spokeCount, 4, 8);
            for (int i = 0; i < count; i++)
            {
                float angle = -90.0f + i * 360.0f / count;
                var pivot = new GameObject($"AodaruSpoke{i}").transform;
                pivot.SetParent(m_spokeWheel, false);
                pivot.localRotation = Quaternion.Euler(0.0f, 0.0f, angle);
                pivot.localScale = new Vector3(0.01f, 1.0f, 1.0f);

                CreateQuad("Spoke", pivot, 0.62f, 2.85f, m_spokeWidth, m_spokeMaterial);
                // 背景の茶色いハンマー影絵は、青い実ハンマーと紛れるため使用しない
                m_spokes.Add(pivot);
            }

            // 集中線は白・少数・細い2段階。顔の中心半径には置かない。
            int lineCount = Mathf.Clamp(m_concentrationLineCount, 4, 16);
            for (int i = 0; i < lineCount; i++)
            {
                float angle = i * 360.0f / lineCount + Random.Range(-8.0f, 8.0f);
                var line = new GameObject($"AodaruConcentration{i}").transform;
                line.SetParent(m_stage.transform, false);
                line.localPosition = new Vector3(0.0f, 1.58f, 0.62f);
                line.localRotation = Quaternion.Euler(0.0f, 0.0f, angle);
                float width = i % 3 == 0 ? m_concentrationLineWidth * 1.8f : m_concentrationLineWidth;
                CreateQuad("Line", line, 1.15f, 5.8f, width, m_concentrationMaterial);
            }
        }

        void CreateQuad(string name, Transform parent, float inner, float outer,
            float width, Material material)
        {
            var vertices = new[]
            {
                new Vector3(inner, -width * 0.5f, 0.0f),
                new Vector3(outer, -width * 0.5f, 0.0f),
                new Vector3(outer, width * 0.5f, 0.0f),
                new Vector3(inner, width * 0.5f, 0.0f),
            };
            var mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            m_backgroundMeshes.Add(mesh);

            var go = new GameObject(name);
            go.layer = CutInLayer;
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void CreateHammerSilhouette(Transform parent, float outer)
        {
            // 単色のハンマーヘッド＋柄。実際のハンマーの茶〜オレンジ系に寄せる。
            var vertices = new[]
            {
                new Vector3(-0.34f, -0.26f, 0.0f),
                new Vector3(0.34f, -0.26f, 0.0f),
                new Vector3(0.46f, 0.26f, 0.0f),
                new Vector3(-0.46f, 0.26f, 0.0f),
                new Vector3(-0.07f, -0.26f, 0.0f),
                new Vector3(0.07f, -0.26f, 0.0f),
                new Vector3(0.07f, -0.78f, 0.0f),
                new Vector3(-0.07f, -0.78f, 0.0f),
            };
            var mesh = new Mesh { name = "AodaruHammerSilhouette" };
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            mesh.RecalculateBounds();
            m_backgroundMeshes.Add(mesh);

            var go = new GameObject("HammerSilhouette");
            go.layer = CutInLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(outer, 0.0f, 0.0f);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (m_hammerSilhouetteMaterial != null) renderer.sharedMaterial = m_hammerSilhouetteMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void AnimateBackground(float age)
        {
            if (m_spokeWheel != null)
            {
                m_spokeWheel.localRotation = Quaternion.Euler(0.0f, 0.0f, m_spokeRotationSpeed * age);
            }

            for (int i = 0; i < m_spokes.Count; i++)
            {
                var spoke = m_spokes[i];
                if (spoke == null) continue;
                float appear = Mathf.Clamp01((age - i * m_spokeAppearStagger) / Mathf.Max(0.01f, m_spokeAppearTime));
                float outro = Mathf.Clamp01((Duration - age) / Mathf.Max(0.01f, m_spokeOutroTime));
                float scaleX = Mathf.SmoothStep(0.01f, 1.0f, appear) * Mathf.SmoothStep(0.01f, 1.0f, outro);
                spoke.localScale = new Vector3(scaleX, 1.0f, 1.0f);
            }

            float lineFade = Mathf.Clamp01(age / 0.18f) * Mathf.Clamp01((Duration - age) / 0.22f);
            if (m_concentrationMaterial != null && m_concentrationMaterial.HasProperty("_BaseColor"))
            {
                Color c = new Color(1.0f, 1.0f, 1.0f, 0.30f * lineFade);
                m_concentrationMaterial.SetColor("_BaseColor", c);
                m_concentrationMaterial.SetColor("_Color", c);
            }
        }

        void Animate(float age)
        {
            if (m_subject != null)
            {
                m_subject.localPosition = new Vector3(
                    Mathf.Sin(age * 1.8f) * 0.035f,
                    1.58f + Mathf.Sin(age * 2.2f) * 0.025f,
                    0.0f);
                m_subject.localRotation = Quaternion.Euler(
                    Mathf.Sin(age * 2.1f) * 1.2f,
                    180.0f + Mathf.Sin(age * 1.5f) * 2.0f,
                    Mathf.Sin(age * 1.7f) * 1.0f);
            }

            for (int i = 0; i < m_arms.Count; i++)
            {
                var arm = m_arms[i];
                if (arm.Hand == null) continue;
                float orbitX = Mathf.Cos(age * arm.OrbitSpeed + arm.Phase) * arm.OrbitRadius;
                float orbitY = Mathf.Sin(age * arm.OrbitSpeed * 1.2f + arm.Phase) * arm.OrbitRadius;
                Vector3 targetHand = arm.HandBase + new Vector3(orbitX, orbitY, 0.0f);

                Vector3 shoulderPoint = new Vector3(
                    Mathf.Cos(arm.BaseAngle * Mathf.Deg2Rad) * 0.42f,
                    1.20f + Mathf.Sin(arm.BaseAngle * Mathf.Deg2Rad) * 0.22f,
                    0.05f + i * 0.01f);

                // 胴体から「にょきっ」と生える。EaseOutBackで少し行き過ぎてから収まる
                float raw = Mathf.Clamp01((age - arm.AppearDelay) / Mathf.Max(0.01f, m_armAppearTime));
                float grow = EaseOutBack(raw);
                float visibleGrow = Mathf.Max(0.01f, grow);
                Vector3 handPoint = Vector3.Lerp(shoulderPoint, targetHand, grow);

                arm.Hand.localPosition = handPoint;
                arm.Hand.localScale = Vector3.one * Mathf.Clamp(visibleGrow, 0.01f, 1.15f);
                // ハンマーが真横を向いて「消えた」ように見えないよう、全体的にカメラ側へ向ける。
                // 奥行きの角度は個体ごとに少し変えて6本の向きの違いを出す。
                float hammerYaw = 180.0f + (i - 2.5f) * 9.0f
                    + Mathf.Sin(age * 1.6f + arm.Phase) * 6.0f;
                arm.Hand.localRotation = Quaternion.Euler(
                    Mathf.Sin(age * 2.0f + arm.Phase) * arm.Tilt,
                    hammerYaw,
                    Mathf.Cos(age * 1.8f + arm.Phase) * arm.Tilt);

                Vector3 direction = handPoint - shoulderPoint;
                float length = Mathf.Max(0.02f, direction.magnitude);
                arm.Cylinder.localPosition = (shoulderPoint + handPoint) * 0.5f;
                arm.Cylinder.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
                arm.Cylinder.localScale = new Vector3(0.055f, length * 0.5f, 0.055f);
            }

            // 6本が出揃う瞬間にカメラを揺らし、画面を青白くフラッシュさせる
            if (!m_impactDone && age >= m_armsImpactTime)
            {
                m_impactDone = true;
                m_impactPulse = 1.0f;
                m_impactFlashUntil = Time.unscaledTime + 0.16f;
            }
            if (m_camera != null)
            {
                if (m_impactPulse > 0.0f)
                {
                    Vector2 shake = Random.insideUnitCircle * (0.18f * m_impactPulse);
                    m_camera.transform.localPosition = m_cameraBasePosition
                        + new Vector3(shake.x, shake.y, Random.Range(-0.08f, 0.08f) * m_impactPulse);
                    m_impactPulse = Mathf.Max(0.0f, m_impactPulse - Time.unscaledDeltaTime * 5.0f);
                }
                else
                {
                    m_camera.transform.localPosition = m_cameraBasePosition;
                }
            }

            AnimateBackground(age);
        }

        static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1.0f;
            return 1.0f + c3 * Mathf.Pow(t - 1.0f, 3.0f) + c1 * Mathf.Pow(t - 1.0f, 2.0f);
        }

        void CreateTitleStyle()
        {
            if (m_stylesReady) return;
            m_stylesReady = true;
            if (s_font == null)
            {
                s_font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP" }, 24);
            }
            if (s_font == null) s_font = GUI.skin.font;

            m_titleStyle = new GUIStyle(GUI.skin.label);
            m_titleStyle.font = s_font;
            m_titleStyle.fontSize = 158;
            m_titleStyle.fontStyle = FontStyle.Bold;
            m_titleStyle.alignment = TextAnchor.MiddleCenter;
            m_titleStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            if (!m_active) return;
            float age = Time.unscaledTime - m_start;
            if (age > Duration + 0.15f) return;

            GUI.depth = -1000;
            if (!m_stylesReady) CreateTitleStyle();
            float fade = Mathf.Clamp01(age / 0.10f) * Mathf.Clamp01((Duration - age) / 0.16f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height),
                m_renderTexture, ScaleMode.ScaleAndCrop, false);

            // 6本が出揃った瞬間の青白いフラッシュ
            float impactFlash = m_impactFlashUntil - Time.unscaledTime;
            if (impactFlash > 0.0f)
            {
                GUI.color = new Color(0.62f, 0.88f, 1.0f,
                    Mathf.Clamp01(impactFlash / 0.16f) * 0.32f);
                GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            GUI.color = new Color(1.0f, 1.0f, 1.0f, fade);
            float intro = Mathf.Clamp01(age / 0.14f);
            float titleScale = Mathf.Lerp(m_titleIntroScale, 1.0f,
                Mathf.SmoothStep(0.0f, 1.0f, intro));
            DrawTitle(new Rect(0.0f, Screen.height * m_titleY, Screen.width, 220.0f),
                m_specialName, fade, titleScale);
            GUI.color = Color.white;
        }

        void DrawTitle(Rect rect, string text, float alpha, float scale)
        {
            if (m_titleStyle == null) return;

            // 登場時に1.4→1.0へ縮みながら、少し傾いて迫る
            Matrix4x4 oldMatrix = GUI.matrix;
            Vector2 pivot = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivot);
            GUIUtility.RotateAroundPivot(m_titleTilt, pivot);

            GUI.color = new Color(0.0f, 0.01f, 0.04f, 0.95f * alpha);
            GUI.Label(new Rect(rect.x + 13.0f, rect.y + 13.0f, rect.width, rect.height), text, m_titleStyle);

            GUI.color = new Color(m_titleAccent.r, m_titleAccent.g, m_titleAccent.b, 0.98f * alpha);
            DrawOutline(rect, text, 10.0f);

            GUI.color = new Color(1.0f, 0.85f, 0.52f, 0.95f * alpha);
            DrawOutline(rect, text, 4.0f);

            GUI.color = new Color(1.0f, 1.0f, 1.0f, alpha);
            GUI.Label(rect, text, m_titleStyle);
            GUI.matrix = oldMatrix;
        }

        void DrawOutline(Rect rect, string text, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2.0f / 8.0f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(a) * radius, rect.y + Mathf.Sin(a) * radius,
                    rect.width, rect.height), text, m_titleStyle);
            }
        }

        void CleanupStage()
        {
            if (m_stage != null) Destroy(m_stage);
            if (m_renderTexture != null)
            {
                m_renderTexture.Release();
                Destroy(m_renderTexture);
            }
            if (m_backdropMaterial != null) Destroy(m_backdropMaterial);
            if (m_armMaterial != null) Destroy(m_armMaterial);
            if (m_trailMaterial != null) Destroy(m_trailMaterial);
            if (m_spokeMaterial != null) Destroy(m_spokeMaterial);
            if (m_hammerSilhouetteMaterial != null) Destroy(m_hammerSilhouetteMaterial);
            if (m_concentrationMaterial != null) Destroy(m_concentrationMaterial);

            m_stage = null;
            m_camera = null;
            m_renderTexture = null;
            m_subject = null;
            m_backdrop = null;
            m_backdropMaterial = null;
            m_armMaterial = null;
            m_trailMaterial = null;
            m_spokeMaterial = null;
            m_hammerSilhouetteMaterial = null;
            m_concentrationMaterial = null;
            if (m_energyHammerMaterial != null) Destroy(m_energyHammerMaterial);
            m_energyHammerMaterial = null;
            m_spokeWheel = null;
            m_spokes.Clear();
            for (int i = 0; i < m_backgroundMeshes.Count; i++)
            {
                if (m_backgroundMeshes[i] != null) Destroy(m_backgroundMeshes[i]);
            }
            m_backgroundMeshes.Clear();
            m_arms.Clear();
            m_createdObjects.Clear();
        }

        static Transform FindPart(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindPart(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // ハンマー本体のDarumaHammer(Awakeで所有者を参照する)を複製せず、
        // 見た目の子オブジェクトだけを複製する。これでAwakeのNullReferenceを防ぐ。
        Transform CreateHammerVisual(Transform source, Transform parent)
        {
            var root = new GameObject("CutInHammer").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = CutInLayer;
            if (source == null) return root;

            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                var clone = Instantiate(child.gameObject, root, false);
                clone.name = child.name;
                clone.transform.localPosition = child.localPosition;
                clone.transform.localRotation = child.localRotation;
                clone.transform.localScale = child.localScale;
                foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
                foreach (var mono in clone.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mono);
                SetLayerRecursively(clone);
            }
            return root;
        }

        GameObject CloneVisual(GameObject source, Transform parent, string name)
        {
            var clone = Instantiate(source, parent, false);
            clone.name = name;
            foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            foreach (var mono in clone.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mono);
            SetLayerRecursively(clone);
            return clone;
        }

        static void SetLayerRecursively(GameObject go)
        {
            go.layer = CutInLayer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursively(go.transform.GetChild(i).gameObject);
        }

        static Texture2D GetBackdropTexture()
        {
            if (s_backdropTexture != null) return s_backdropTexture;

            const int size = 512;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            // 同心円・破片・粒子を廃止し、黒〜濃紺の放射ビネットだけにする
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), center) / max;
                    float t = Mathf.Clamp01(r);
                    Color color = Color.Lerp(new Color(0.090f, 0.200f, 0.400f),
                        new Color(0.015f, 0.040f, 0.100f), Mathf.SmoothStep(0.0f, 1.0f, t));
                    float glow = Mathf.Pow(Mathf.Clamp01(1.0f - r), 3.0f);
                    color += new Color(glow * 0.10f, glow * 0.20f, glow * 0.38f);
                    pixels[y * size + x] = color;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_backdropTexture = texture;
            return texture;
        }

        static void FillTaperedSegment(Color32[] pixels, int size, Vector2 a, Vector2 b,
            float widthA, float widthB, Color color)
        {
            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            var polygon = new Vector2[4];
            polygon[0] = a + normal * widthA * 0.5f;
            polygon[1] = b + normal * widthB * 0.5f;
            polygon[2] = b - normal * widthB * 0.5f;
            polygon[3] = a - normal * widthA * 0.5f;
            FillPolygon(pixels, size, polygon, color);
        }

        static void DrawCircleLine(Color32[] pixels, int size, Vector2 center, float radius,
            float width, Color color)
        {
            const int segments = 160;
            Vector2 previous = center + new Vector2(radius, 0.0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2.0f / segments;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                FillTaperedSegment(pixels, size, previous, next, width, width, color);
                previous = next;
            }
        }

        static void DrawCircle(Color32[] pixels, int size, Vector2 center, float radius, Color color)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(center.x - radius), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(center.x + radius), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(center.y - radius), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(center.y + radius), 0, size - 1);
            float sqr = radius * radius;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    if (dx * dx + dy * dy > sqr) continue;
                    int index = y * size + x;
                    Color32 old = pixels[index];
                    pixels[index] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(old.r + color.r * 120.0f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(old.g + color.g * 160.0f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(old.b + color.b * 180.0f), 0, 255),
                        255);
                }
            }
        }

        static void FillPolygon(Color32[] pixels, int size, Vector2[] polygon, Color color)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < polygon.Length; i++)
            {
                minY = Mathf.Min(minY, polygon[i].y);
                maxY = Mathf.Max(maxY, polygon[i].y);
            }
            int startY = Mathf.Clamp(Mathf.FloorToInt(minY), 0, size - 1);
            int endY = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, size - 1);
            var intersections = new List<float>(8);
            for (int y = startY; y <= endY; y++)
            {
                float scanY = y + 0.5f;
                intersections.Clear();
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 a = polygon[i];
                    Vector2 b = polygon[(i + 1) % polygon.Length];
                    if ((a.y <= scanY && b.y > scanY) || (b.y <= scanY && a.y > scanY))
                    {
                        float t = (scanY - a.y) / (b.y - a.y);
                        intersections.Add(a.x + (b.x - a.x) * t);
                    }
                }
                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int startX = Mathf.Clamp(Mathf.CeilToInt(intersections[i]), 0, size - 1);
                    int endX = Mathf.Clamp(Mathf.FloorToInt(intersections[i + 1]), 0, size - 1);
                    for (int x = startX; x <= endX; x++) pixels[y * size + x] = color;
                }
            }
        }
    }
}
