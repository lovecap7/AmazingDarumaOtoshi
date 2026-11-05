using System.Collections.Generic;
using Nakahira;
using Nakagawa;
using UnityEngine;

namespace Yurube
{
    // だるきんぐの必殺技「エクスカリバー」。
    // 詠唱中に移動入力で方向を決め、任意の行動ボタンまたは詠唱時間経過で確定。
    // カットイン終了後、確定方向へ直線状の即死攻撃を1回だけ放つ。
    [RequireComponent(typeof(DarumaCharacter))]
    public class DaruKingSpecial : MonoBehaviour, ISpecialAbility
    {
        [Header("詠唱")]
        [SerializeField] string m_specialName = "エクスカリバー";
        [Tooltip("自動で詠唱が完了するまでの時間(秒)")]
        [SerializeField, Min(0.1f)] float m_chargeDuration = 2.0f;
        [Tooltip("発動直後の入力を詠唱完了として拾わないための最小時間(秒)")]
        [SerializeField, Min(0.0f)] float m_anyButtonMinTime = 0.15f;
        [Tooltip("攻撃判定の長さ(m)")]
        [SerializeField, Min(0.5f)] float m_attackLength = 14.0f;
        [Tooltip("攻撃判定の太さ(m)")]
        [SerializeField, Min(0.1f)] float m_attackWidth = 3.2f;
        [Tooltip("攻撃エフェクトの表示時間(秒)")]
        [SerializeField, Min(0.2f)] float m_effectDuration = 1.0f;
        [Tooltip("発動ログをConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        enum State
        {
            Idle,
            Charging,
            CutIn,
        }

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        State m_state = State.Idle;
        float m_stateTime;
        bool m_attackApplied;

        Vector3 m_attackOrigin;
        Vector3 m_attackDirection = Vector3.forward;

        GameObject m_guide;
        Material m_guideMaterial;
        LineRenderer m_guideLine;
        Material m_guideLineMaterial;

        class ChargeSpark
        {
            public Transform Transform;
            public float BaseAngle;
            public float Phase;
            public float Speed;
            public float Size;
        }

        GameObject m_chargeEffect;
        Material m_chargeMaterial;
        Transform m_chargeOrb;
        Light m_chargeLight;
        readonly List<ChargeSpark> m_chargeSparks = new List<ChargeSpark>();

        GameObject m_beam;
        float m_beamElapsed;
        Material m_beamGoldMaterial;
        Material m_beamWhiteMaterial;
        Material m_beamCurtainMaterial;
        Material m_beamLineMaterial;

        float m_attackFlashUntil;
        float m_attackFlashDuration = 0.30f;
        float m_attackFlashStrength = 0.55f;

        GUIStyle m_chargeStyle;
        Font m_uiFont;
        bool m_uiReady;

        public bool IsActive => m_state != State.Idle;
        public bool IsCharging => m_state == State.Charging;
        public float ChargeRemaining => m_state == State.Charging
            ? Mathf.Max(0.0f, m_chargeDuration - m_stateTime)
            : 0.0f;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
        }

        private void Update()
        {
            if (m_character == null || m_gauge == null) return;

            if (m_beam != null) UpdateBeamEffect(Time.deltaTime);

            if (m_state == State.Idle)
            {
                if (SpecialInput.WasPressed(m_character)) TryActivate();
                return;
            }

            if (m_state == State.Charging)
            {
                m_stateTime += Time.deltaTime;
                UpdateGuide();
                UpdateChargeEffect();

                bool anyButton = m_stateTime >= m_anyButtonMinTime
                    && SpecialInput.WasAnyActionPressed(m_character);
                if (anyButton || m_stateTime >= m_chargeDuration)
                    CompleteCharge();
                return;
            }

            if (m_state == State.CutIn && !DaruKingCutIn.IsPlaying)
            {
                FireExcalibur();
                m_state = State.Idle;
            }
        }

        public bool TryActivate()
        {
            if (m_state != State.Idle) return false;
            if (m_character == null || m_gauge == null || !m_gauge.IsFull) return false;
            if (!IsLocalHuman()) return false;

            m_gauge.Consume();
            m_state = State.Charging;
            m_stateTime = 0.0f;
            m_attackApplied = false;
            CreateGuide();

            if (m_logSpecial) Debug.Log("[だるきんぐ] エクスカリバー詠唱開始", this);
            return true;
        }

        void CompleteCharge()
        {
            if (m_state != State.Charging || m_character == null) return;

            m_attackDirection = HorizontalForward();
            m_attackOrigin = m_character.transform.position;
            DestroyGuide();

            m_state = State.CutIn;
            m_stateTime = 0.0f;

            GameObject subject = m_character.Stack != null && m_character.Stack.Head != null
                ? m_character.Stack.Head.gameObject
                : m_character.gameObject;
            DaruKingCutIn.Show(m_specialName, subject);

            if (m_logSpecial)
                Debug.Log($"[だるきんぐ] エクスカリバー方向確定 {m_attackDirection}", this);
        }

        Vector3 HorizontalForward()
        {
            Vector3 direction = m_character.transform.forward;
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            return direction.normalized;
        }

        bool IsLocalHuman()
        {
            if (DarumaMatch.Current == null) return true;
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) return !p.IsCpu;
            }
            return false;
        }

        //========================================
        // 詠唱中の攻撃予定方向ガイド
        //========================================

        void CreateGuide()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "DaruKingExcaliburGuide";
            var collider = cube.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            m_guideMaterial = CreateEffectMaterial(new Color(1.0f, 0.82f, 0.30f, 0.30f));
            var renderer = cube.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (m_guideMaterial != null) renderer.sharedMaterial = m_guideMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var lineObject = new GameObject("DaruKingExcaliburGuideLine");
            m_guideLine = lineObject.AddComponent<LineRenderer>();
            m_guideLine.positionCount = 2;
            m_guideLine.useWorldSpace = true;
            m_guideLine.widthMultiplier = 0.08f;
            m_guideLine.alignment = LineAlignment.View;
            m_guideLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_guideLine.receiveShadows = false;
            m_guideLineMaterial = CreateEffectMaterial(new Color(1.0f, 0.95f, 0.65f, 0.85f));
            if (m_guideLineMaterial != null) m_guideLine.sharedMaterial = m_guideLineMaterial;
            m_guideLine.startColor = new Color(1.0f, 0.95f, 0.65f, 0.85f);
            m_guideLine.endColor = new Color(1.0f, 0.95f, 0.65f, 0.85f);

            m_guide = cube;
            CreateChargeEffect();
            UpdateGuide();
        }

        void UpdateGuide()
        {
            if (m_guide == null || m_character == null) return;

            Vector3 direction = HorizontalForward();
            Vector3 origin = m_character.transform.position;
            Vector3 center = origin + direction * (m_attackLength * 0.5f) + Vector3.up * 0.06f;
            m_guide.transform.position = center;
            m_guide.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            m_guide.transform.localScale = new Vector3(m_attackWidth, 0.03f, m_attackLength);

            if (m_guideLine != null)
            {
                Vector3 start = origin + Vector3.up * 0.10f;
                Vector3 end = origin + direction * m_attackLength + Vector3.up * 0.10f;
                m_guideLine.SetPosition(0, start);
                m_guideLine.SetPosition(1, end);
            }
        }

        void CreateChargeEffect()
        {
            m_chargeEffect = new GameObject("DaruKingChargeGather");
            m_chargeMaterial = CreateEffectMaterial(new Color(1.0f, 0.82f, 0.28f, 0.88f));

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "ChargeOrb";
            var orbCollider = orb.GetComponent<Collider>();
            if (orbCollider != null) Destroy(orbCollider);
            orb.transform.SetParent(m_chargeEffect.transform, false);
            orb.transform.localScale = Vector3.one * 0.48f;
            var orbRenderer = orb.GetComponent<MeshRenderer>();
            if (orbRenderer != null)
            {
                if (m_chargeMaterial != null) orbRenderer.sharedMaterial = m_chargeMaterial;
                orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                orbRenderer.receiveShadows = false;
            }
            m_chargeOrb = orb.transform;

            for (int i = 0; i < 16; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = $"ChargeSpark{i}";
                var collider = spark.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                spark.transform.SetParent(m_chargeEffect.transform, false);
                spark.transform.localScale = Vector3.one * 0.08f;
                var renderer = spark.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    if (m_chargeMaterial != null) renderer.sharedMaterial = m_chargeMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                m_chargeSparks.Add(new ChargeSpark
                {
                    Transform = spark.transform,
                    BaseAngle = i * Mathf.PI * 2.0f / 16.0f,
                    Phase = Random.Range(0.0f, 1.0f),
                    Speed = Random.Range(0.22f, 0.40f),
                    Size = Random.Range(0.06f, 0.15f),
                });
            }

            var lightObject = new GameObject("ChargeLight");
            lightObject.transform.SetParent(m_chargeEffect.transform, false);
            m_chargeLight = lightObject.AddComponent<Light>();
            m_chargeLight.type = LightType.Point;
            m_chargeLight.color = new Color(1.0f, 0.84f, 0.38f);
            m_chargeLight.intensity = 3.0f;
            m_chargeLight.range = 6.0f;
            m_chargeLight.shadows = LightShadows.None;
        }

        void UpdateChargeEffect()
        {
            if (m_chargeEffect == null || m_character == null) return;

            Vector3 center = m_character.transform.position
                + Vector3.up * 1.25f
                + HorizontalForward() * 0.55f;
            m_chargeEffect.transform.position = center;

            float intro = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(m_stateTime / 0.18f));
            m_chargeEffect.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.0f, intro);

            if (m_chargeOrb != null)
            {
                float pulse = 0.44f + 0.09f * Mathf.Sin(Time.time * 12.0f);
                m_chargeOrb.localScale = Vector3.one * pulse;
            }
            if (m_chargeLight != null)
                m_chargeLight.intensity = 2.4f + 1.2f * intro;

            for (int i = 0; i < m_chargeSparks.Count; i++)
            {
                var spark = m_chargeSparks[i];
                if (spark.Transform == null) continue;

                float cycle = Mathf.Repeat(Time.time * spark.Speed + spark.Phase, 1.0f);
                float angle = spark.BaseAngle + cycle * Mathf.PI * 3.0f;
                float radius = Mathf.Lerp(3.2f, 0.12f, cycle);
                spark.Transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(cycle * Mathf.PI) * 0.75f,
                    Mathf.Sin(angle) * radius * 0.38f);
                float scale = spark.Size * (1.0f - cycle * 0.75f) * intro;
                spark.Transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                spark.Transform.rotation = Camera.main != null
                    ? Camera.main.transform.rotation
                    : Quaternion.identity;
            }
        }

        void DestroyChargeEffect()
        {
            if (m_chargeEffect != null) Destroy(m_chargeEffect);
            if (m_chargeMaterial != null) Destroy(m_chargeMaterial);
            m_chargeEffect = null;
            m_chargeMaterial = null;
            m_chargeOrb = null;
            m_chargeLight = null;
            m_chargeSparks.Clear();
        }

        void DestroyGuide()
        {
            DestroyChargeEffect();
            if (m_guide != null) Destroy(m_guide);
            if (m_guideMaterial != null) Destroy(m_guideMaterial);
            if (m_guideLine != null) Destroy(m_guideLine.gameObject);
            if (m_guideLineMaterial != null) Destroy(m_guideLineMaterial);
            m_guide = null;
            m_guideMaterial = null;
            m_guideLine = null;
            m_guideLineMaterial = null;
        }

        //========================================
        // カットイン後の直線攻撃
        //========================================

        void FireExcalibur()
        {
            if (m_attackApplied) return;
            m_attackApplied = true;

            m_attackFlashUntil = Time.unscaledTime + m_attackFlashDuration;
            m_attackFlashStrength = 0.55f;

            CreateBeam();
            ApplyExcaliburDamage();

            // あかだると同じ焦げシェーダーで、斬撃ラインの地面を焼く
            GroundScorch.SpawnLine(
                m_attackOrigin + Vector3.up * 0.02f,
                m_attackOrigin + m_attackDirection * m_attackLength + Vector3.up * 0.02f,
                m_attackWidth, 6.0f, 2.5f,
                new Color(0.055f, 0.04f, 0.03f),
                new Color(1.0f, 0.72f, 0.12f),
                0.55f);

            var camera = Camera.main;
            if (camera != null)
            {
                var shake = camera.GetComponent<CameraShake>();
                if (shake != null)
                    shake.Shake(new CameraShake.ShakeSettings(0.85f, 0.65f));
            }

            if (m_logSpecial)
                Debug.Log($"[だるきんぐ] エクスカリバー発射 長さ{m_attackLength:0.0}m 太さ{m_attackWidth:0.0}m", this);
        }

        void CreateBeam()
        {
            m_beam = new GameObject("DaruKingExcaliburSword");
            m_beam.transform.position = m_attackOrigin;
            m_beam.transform.rotation = Quaternion.LookRotation(m_attackDirection, Vector3.up);
            m_beam.layer = 0;
            m_beamElapsed = 0.0f;
            m_beam.transform.localScale = new Vector3(0.35f, 0.15f, 1.0f);

            m_beamGoldMaterial = CreateEffectMaterial(new Color(1.0f, 0.76f, 0.16f, 0.78f));
            m_beamWhiteMaterial = CreateEffectMaterial(new Color(1.0f, 1.0f, 0.92f, 0.95f));
            m_beamCurtainMaterial = CreateEffectMaterial(new Color(1.0f, 0.88f, 0.48f, 0.11f));
            m_beamLineMaterial = CreateEffectMaterial(new Color(1.0f, 1.0f, 0.90f, 0.95f));

            // 巨大な聖剣そのものを攻撃範囲いっぱいに投影する
            CreateBeamPart("Blade", new Vector3(0.0f, 0.72f, m_attackLength * 0.5f),
                new Vector3(m_attackWidth, 0.50f, m_attackLength), m_beamGoldMaterial);
            CreateBeamPart("BladeCore", new Vector3(0.0f, 0.78f, m_attackLength * 0.5f),
                new Vector3(m_attackWidth * 0.27f, 0.22f, m_attackLength * 0.98f), m_beamWhiteMaterial);
            CreateBeamPart("Guard", new Vector3(0.0f, 0.72f, -0.30f),
                new Vector3(m_attackWidth * 1.35f, 0.30f, 0.30f), m_beamGoldMaterial);
            CreateBeamPart("Grip", new Vector3(0.0f, 0.72f, -0.85f),
                new Vector3(0.18f, 0.18f, 0.80f), m_beamGoldMaterial);
            CreateBeamPart("Pommel", new Vector3(0.0f, 0.72f, -1.28f),
                new Vector3(0.34f, 0.34f, 0.34f), m_beamWhiteMaterial);

            // 聖剣から立ち上る光の幕と光柱
            CreateBeamPart("LightCurtain", new Vector3(0.0f, 1.90f, m_attackLength * 0.5f),
                new Vector3(m_attackWidth * 1.15f, 3.8f, m_attackLength), m_beamCurtainMaterial);
            for (int i = 0; i < 5; i++)
            {
                float z = m_attackLength * (i + 1) / 6.0f;
                CreateBeamPart($"LightPillar{i}", new Vector3(0.0f, 1.70f, z),
                    new Vector3(0.12f, 3.4f, 0.12f),
                    i % 2 == 0 ? m_beamWhiteMaterial : m_beamGoldMaterial);
            }

            CreateBeamEdgeLine("EdgeLeft", -m_attackWidth * 0.5f);
            CreateBeamEdgeLine("EdgeRight", m_attackWidth * 0.5f);
            CreateBeamEdgeLine("Center", 0.0f);

            GimmickShockwave.Create(m_attackOrigin + Vector3.up * 0.03f,
                new Color(1.0f, 0.82f, 0.30f, 0.95f), m_attackWidth * 2.0f, 0.5f);
            GimmickShockwave.Create(m_attackOrigin + m_attackDirection * m_attackLength + Vector3.up * 0.03f,
                new Color(1.0f, 1.0f, 0.85f, 0.9f), m_attackWidth * 2.4f, 0.6f);

            // 出現フレームでいきなり全開にならないよう、透明・縮小から始める
            SetBeamAlpha(0.0f);
        }

        void CreateBeamPart(string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            var collider = cube.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            cube.transform.SetParent(m_beam.transform, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.layer = 0;

            var renderer = cube.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void CreateBeamEdgeLine(string name, float x)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(m_beam.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.widthMultiplier = name == "Center" ? 0.14f : 0.075f;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (m_beamLineMaterial != null) line.sharedMaterial = m_beamLineMaterial;
            line.startColor = new Color(1.0f, 1.0f, 0.90f, 0.95f);
            line.endColor = new Color(1.0f, 1.0f, 0.90f, 0.95f);
            line.SetPosition(0, new Vector3(x, 1.0f, 0.0f));
            line.SetPosition(1, new Vector3(x, 1.0f, m_attackLength));
        }

        void UpdateBeamEffect(float dt)
        {
            if (m_beam == null) return;

            m_beamElapsed += dt;
            float duration = Mathf.Max(0.2f, m_effectDuration);
            float fadeIn = Mathf.Clamp01(m_beamElapsed / 0.12f);
            float fadeOut = Mathf.Clamp01((duration - m_beamElapsed) / 0.32f);
            float alpha = Mathf.Min(fadeIn, fadeOut);
            SetBeamAlpha(alpha);

            float grow = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(m_beamElapsed / 0.16f));
            m_beam.transform.localScale = new Vector3(
                Mathf.Lerp(0.35f, 1.0f, grow),
                Mathf.Lerp(0.15f, 1.0f, grow),
                1.0f);

            if (m_beamElapsed >= duration) ClearBeam();
        }

        void SetBeamAlpha(float alpha)
        {
            SetMaterialAlpha(m_beamGoldMaterial, 0.78f * alpha);
            SetMaterialAlpha(m_beamWhiteMaterial, 0.95f * alpha);
            SetMaterialAlpha(m_beamCurtainMaterial, 0.11f * alpha);
            SetMaterialAlpha(m_beamLineMaterial, 0.95f * alpha);

            if (m_beam == null) return;
            foreach (var line in m_beam.GetComponentsInChildren<LineRenderer>())
            {
                if (line == null) continue;
                Color color = new Color(1.0f, 1.0f, 0.90f, 0.95f * alpha);
                line.startColor = color;
                line.endColor = color;
            }
        }

        void SetMaterialAlpha(Material material, float alpha)
        {
            if (material == null) return;
            if (material.HasProperty("_Color"))
            {
                Color color = material.GetColor("_Color");
                color.a = alpha;
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_BaseColor"))
            {
                Color color = material.GetColor("_BaseColor");
                color.a = alpha;
                material.SetColor("_BaseColor", color);
            }
        }

        void ApplyExcaliburDamage()
        {
            float halfWidth = m_attackWidth * 0.5f;

            foreach (var character in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (character == null || character == m_character) continue;
                if (character.IsInvincible) continue;

                Vector3 delta = character.transform.position - m_attackOrigin;
                delta.y = 0.0f;
                float along = Vector3.Dot(delta, m_attackDirection);
                if (along < 0.0f || along > m_attackLength) continue;

                Vector3 closest = m_attackOrigin + m_attackDirection * along;
                Vector3 offset = character.transform.position - closest;
                offset.y = 0.0f;
                if (offset.sqrMagnitude > halfWidth * halfWidth) continue;

                character.Eliminate(EliminationCause.Other, Vector3.up, character.transform.position);
            }
        }

        Material CreateEffectMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        void ClearBeam()
        {
            if (m_beam != null) Destroy(m_beam);
            if (m_beamGoldMaterial != null) Destroy(m_beamGoldMaterial);
            if (m_beamWhiteMaterial != null) Destroy(m_beamWhiteMaterial);
            if (m_beamCurtainMaterial != null) Destroy(m_beamCurtainMaterial);
            if (m_beamLineMaterial != null) Destroy(m_beamLineMaterial);
            m_beam = null;
            m_beamElapsed = 0.0f;
            m_beamGoldMaterial = null;
            m_beamWhiteMaterial = null;
            m_beamCurtainMaterial = null;
            m_beamLineMaterial = null;
        }

        private void OnDisable()
        {
            DestroyGuide();
            ClearBeam();
            m_state = State.Idle;
        }

        private void OnDestroy()
        {
            DestroyGuide();
            ClearBeam();
        }

        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character);

            // 発射フラッシュ
            float flashRemaining = m_attackFlashUntil - Time.unscaledTime;
            if (flashRemaining > 0.0f)
            {
                float alpha = Mathf.Clamp01(flashRemaining / Mathf.Max(0.01f, m_attackFlashDuration))
                    * m_attackFlashStrength;
                GUI.color = new Color(1.0f, 0.92f, 0.58f, alpha);
                GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (m_state != State.Charging) return;

            EnsureChargeStyles();
            float scale = Mathf.Max(0.5f, Screen.height / 1080.0f);
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(scale, scale, 1.0f));

            float w = Screen.width / scale;
            float h = Screen.height / scale;
            float remaining = ChargeRemaining;
            float progress = Mathf.Clamp01(1.0f - remaining / Mathf.Max(0.01f, m_chargeDuration));

            var panel = new Rect(w * 0.5f - 240.0f, h - 245.0f, 480.0f, 74.0f);
            GUI.color = new Color(0.02f, 0.03f, 0.08f, 0.86f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(1.0f, 0.80f, 0.25f, 0.95f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2.0f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.x, panel.y + panel.height - 2.0f, panel.width, 2.0f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.x, panel.y, 2.0f, panel.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(panel.x + panel.width - 2.0f, panel.y, 2.0f, panel.height), Texture2D.whiteTexture);

            GUI.color = new Color(1.0f, 0.94f, 0.72f);
            GUI.Label(new Rect(panel.x + 12.0f, panel.y + 5.0f, panel.width - 24.0f, 30.0f),
                $"エクスカリバー  詠唱 {remaining:0.0}s", m_chargeStyle);

            var bar = new Rect(panel.x + 12.0f, panel.y + 42.0f, panel.width - 24.0f, 18.0f);
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.8f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(1.0f, 0.78f, 0.22f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * progress, bar.height),
                Texture2D.whiteTexture);
            GUI.color = new Color(1.0f, 1.0f, 0.92f, 0.55f);
            GUI.DrawTexture(new Rect(bar.x + bar.width * progress - 2.0f, bar.y, 4.0f, bar.height),
                Texture2D.whiteTexture);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        void EnsureChargeStyles()
        {
            if (m_uiReady) return;
            m_uiReady = true;
            m_uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP" }, 24);
            if (m_uiFont == null) m_uiFont = GUI.skin.font;

            m_chargeStyle = new GUIStyle(GUI.skin.label);
            m_chargeStyle.font = m_uiFont;
            m_chargeStyle.fontSize = 22;
            m_chargeStyle.fontStyle = FontStyle.Bold;
            m_chargeStyle.alignment = TextAnchor.MiddleCenter;
            m_chargeStyle.normal.textColor = Color.white;
        }
    }
}
