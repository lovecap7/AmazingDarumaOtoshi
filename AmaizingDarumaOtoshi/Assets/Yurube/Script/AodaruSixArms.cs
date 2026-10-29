using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あおだるの必殺技「六腕」の見た目と判定。
    // ・体から切り離した「浮遊するエネルギーハンマー」を5本出す(手持ちの1本と合わせて6本)
    // ・出現時は小さな光から広がるように、終了時は縮んで消えるように見せる
    // ・手持ちのハンマーも必殺技中だけエネルギーの見た目に差し替え、終わったら戻す
    // ・ハンマーの軌跡(残像)を TrailRenderer で描く
    // ・プレイヤーを起点に3方向を決め、その方向に近づく積み木をはじき返す
    public class AodaruSixArms : MonoBehaviour
    {
        [Tooltip("浮遊ハンマーの大きさ")]
        [SerializeField] float m_floatScale = 1.0f;
        [Tooltip("浮遊ハンマーの軌跡の長さ(秒)")]
        [SerializeField] float m_trailTime = 0.4f;
        [Tooltip("浮遊ハンマーの軌跡の太さ")]
        [SerializeField] float m_trailWidth = 0.55f;
        [Tooltip("出現にかける時間(秒)")]
        [SerializeField] float m_introTime = 0.25f;
        [Tooltip("消えるのにかける時間(秒)")]
        [SerializeField] float m_outroTime = 0.35f;
        [Tooltip("はじき返す範囲(m)")]
        [SerializeField] float m_knockRadius = 3.0f;
        [Tooltip("はじき返す速さ(m/s)")]
        [SerializeField] float m_knockSpeed = 24.0f;
        [Tooltip("1方向あたりの判定の広さ(度、半分)")]
        [SerializeField] float m_sectorHalfAngle = 60.0f;
        [Tooltip("同じ積み木を続けてはじかない間隔(秒)")]
        [SerializeField] float m_knockInterval = 0.5f;

        // 3方向(120度ずつ)
        static readonly float[] kSectorOffsets = { 0.0f, 120.0f, 240.0f };

        // 浮遊ハンマー5本の配置(わざと不揃いにして、虫のような対称性を避ける)
        static readonly float[] kFloatAngles = { 20.0f, 95.0f, 160.0f, 235.0f, 300.0f };
        static readonly float[] kFloatRadii = { 1.15f, 1.30f, 1.05f, 1.25f, 1.10f };
        static readonly float[] kFloatHeights = { 0.80f, 1.05f, 0.90f, 0.72f, 1.12f };
        static readonly float[] kFloatOrbitSpeeds = { 55.0f, -70.0f, 85.0f, -60.0f, 75.0f };

        DarumaCharacter m_character;
        Material m_hammerMaterial;
        Material m_trailMaterial;
        float m_duration = 4.0f;
        float m_elapsed;
        float m_baseYaw;

        readonly List<Transform> m_floats = new List<Transform>(5);
        readonly List<Renderer> m_defaultRenderers = new List<Renderer>(2);
        readonly List<Material> m_defaultMaterials = new List<Material>(2);
        readonly List<Transform> m_defaultTransforms = new List<Transform>(2);
        readonly List<Vector3> m_defaultScales = new List<Vector3>(2);

        readonly Collider[] m_overlaps = new Collider[32];
        readonly Dictionary<TumikiBlock, float> m_lastKnockTimes = new Dictionary<TumikiBlock, float>();

        public void Initialize(DarumaCharacter character, float duration, float baseYaw,
                               float knockRadius, float knockSpeed, float knockInterval,
                               Material energySource)
        {
            m_character = character;
            m_duration = Mathf.Max(0.1f, duration);
            m_baseYaw = baseYaw;
            m_knockRadius = Mathf.Max(0.5f, knockRadius);
            m_knockSpeed = Mathf.Max(1.0f, knockSpeed);
            m_knockInterval = Mathf.Max(0.05f, knockInterval);

            transform.position = character != null ? character.transform.position : Vector3.zero;
            transform.rotation = Quaternion.Euler(0.0f, m_baseYaw, 0.0f);

            m_hammerMaterial = CreateHammerMaterial();
            m_trailMaterial = CreateTrailMaterial(energySource);
            SwapPlayerHammerMaterial();
            BuildFloatingHammers();
        }

        // ハンマー本体用: テクスチャなしの加算合成で、輪郭がはっきり出る青白い光にする
        private static Material CreateHammerMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            var material = new Material(shader);
            material.name = "AodaruEnergyHammer";
            material.SetFloat("_Surface", 1.0f);
            material.SetFloat("_Blend", 2.0f);   // Additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0.0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // 1を超える明るさにして、加算合成で白っぽく光らせる
            material.SetColor("_BaseColor", new Color(0.50f, 1.10f, 1.80f, 1.0f));
            return material;
        }

        // 残像用: 頂点カラーでフェードできるパーティクル用マテリアル
        private static Material CreateTrailMaterial(Material source)
        {
            Material material = null;
            if (source != null)
            {
                material = new Material(source);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) return null;

                material = new Material(shader);
                material.SetFloat("_Surface", 1.0f);
                material.SetFloat("_Blend", 2.0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_ZWrite", 0.0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            material.name = "AodaruEnergyTrail";
            var color = new Color(0.45f, 0.95f, 1.60f, 1.0f);
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            return material;
        }

        // 手持ちのハンマーを必殺技中だけエネルギーの見た目にする
        private void SwapPlayerHammerMaterial()
        {
            if (m_character == null || m_character.Hammer == null || m_hammerMaterial == null) return;

            foreach (var renderer in m_character.Hammer.GetComponentsInChildren<Renderer>())
            {
                m_defaultRenderers.Add(renderer);
                m_defaultMaterials.Add(renderer.sharedMaterial);
                renderer.sharedMaterial = m_hammerMaterial;

                var t = renderer.transform;
                m_defaultTransforms.Add(t);
                m_defaultScales.Add(t.localScale);
            }
        }

        private void RestorePlayerHammerMaterial()
        {
            for (int i = 0; i < m_defaultRenderers.Count; i++)
            {
                var renderer = m_defaultRenderers[i];
                if (renderer != null) renderer.sharedMaterial = m_defaultMaterials[i];
            }
            for (int i = 0; i < m_defaultTransforms.Count; i++)
            {
                var t = m_defaultTransforms[i];
                if (t != null) t.localScale = m_defaultScales[i];
            }

            m_defaultRenderers.Clear();
            m_defaultMaterials.Clear();
            m_defaultTransforms.Clear();
            m_defaultScales.Clear();
        }

        // 浮遊するエネルギーハンマーを5本つくる
        private void BuildFloatingHammers()
        {
            var source = m_character != null && m_character.Hammer != null
                ? m_character.Hammer.transform
                : null;
            if (source == null) return;

            for (int i = 0; i < kFloatAngles.Length; i++)
            {
                var pivot = new GameObject($"EnergyHammer{i}").transform;
                pivot.SetParent(transform, false);
                pivot.localScale = Vector3.one * 0.01f;

                // 見た目は手持ちハンマーの Handle / HammerHead を複製
                for (int c = 0; c < source.childCount; c++)
                {
                    var part = source.GetChild(c);
                    var clone = Instantiate(part.gameObject, pivot, false);
                    clone.name = part.name;
                    clone.transform.localPosition = part.localPosition * m_floatScale;
                    clone.transform.localRotation = part.localRotation;

                    // 柄が細いと「虫の脚」に見えるので、エネルギーの帯っぽく太らせる
                    Vector3 partScale = part.localScale * m_floatScale;
                    if (part.name.Contains("Handle"))
                    {
                        partScale.x *= 2.6f;
                        partScale.y *= 2.6f;
                    }
                    clone.transform.localScale = partScale;

                    if (m_hammerMaterial != null)
                    {
                        foreach (var renderer in clone.GetComponentsInChildren<Renderer>())
                        {
                            renderer.sharedMaterial = m_hammerMaterial;
                        }
                    }
                }

                // ハンマーの軌跡(残像)
                var trailObject = new GameObject("Trail");
                trailObject.transform.SetParent(pivot, false);
                trailObject.transform.localPosition = new Vector3(0.0f, 0.0f, 1.2f * m_floatScale);

                var trail = trailObject.AddComponent<TrailRenderer>();
                if (m_trailMaterial != null) trail.sharedMaterial = m_trailMaterial;
                trail.time = m_trailTime;
                trail.minVertexDistance = 0.03f;
                trail.autodestruct = false;
                trail.emitting = true;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.numCapVertices = 2;
                trail.numCornerVertices = 2;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.startWidth = m_trailWidth;
                trail.endWidth = 0.0f;
                trail.startColor = new Color(0.85f, 1.0f, 1.0f, 1.0f);
                trail.endColor = new Color(0.20f, 0.55f, 1.0f, 0.0f);

                m_floats.Add(pivot);
            }
        }

        private void Update()
        {
            if (m_character == null)
            {
                Cleanup();
                Destroy(gameObject);
                return;
            }

            m_elapsed += Time.deltaTime;
            float total = Mathf.Max(0.01f, m_introTime) + m_duration + Mathf.Max(0.01f, m_outroTime);
            if (m_elapsed >= total)
            {
                Cleanup();
                Destroy(gameObject);
                return;
            }

            // プレイヤーの位置には追従するが、向き(3方向)は発動時のまま固定
            transform.position = m_character.transform.position;

            float appear = EvaluateAppear(out float intensity);

            // ハンマーの光を脈打たせつつ、出現・消滅で明るさを変える
            if (m_hammerMaterial != null)
            {
                float pulse = 1.0f + 0.18f * Mathf.Sin(Time.time * 9.0f);
                Color color = new Color(0.50f, 1.10f, 1.80f, 1.0f) * (intensity * pulse);
                color.a = 1.0f;
                m_hammerMaterial.SetColor("_BaseColor", color);
            }

            // 手持ちのハンマーも同じように拡大・縮小して「実体化」させる
            for (int i = 0; i < m_defaultTransforms.Count; i++)
            {
                var t = m_defaultTransforms[i];
                if (t != null) t.localScale = m_defaultScales[i] * Mathf.Max(0.01f, appear);
            }

            float radiusFactor = 0.35f + 0.65f * Mathf.Clamp01(appear);
            float heightFactor = 0.6f + 0.4f * Mathf.Clamp01(appear);

            for (int i = 0; i < m_floats.Count; i++)
            {
                float yaw = kFloatAngles[i] + kFloatOrbitSpeeds[i] * m_elapsed;
                float bob = Mathf.Sin(Time.time * (1.4f + i * 0.21f) + i) * 0.12f;
                float radians = yaw * Mathf.Deg2Rad;

                m_floats[i].localPosition = new Vector3(
                    Mathf.Sin(radians) * kFloatRadii[i] * radiusFactor,
                    kFloatHeights[i] * heightFactor + bob,
                    Mathf.Cos(radians) * kFloatRadii[i] * radiusFactor);

                float tilt = -25.0f + 15.0f * Mathf.Sin(Time.time * 2.0f + i * 1.3f);
                m_floats[i].localRotation =
                    Quaternion.Euler(0.0f, yaw, 0.0f) *
                    Quaternion.Euler(tilt, 0.0f, 0.0f);

                m_floats[i].localScale = Vector3.one * Mathf.Max(0.01f, appear);
            }
        }

        // 0=出ていない, 1=出ている。出現は少しオーバーシュート、消滅は縮みながら薄くなる
        private float EvaluateAppear(out float intensity)
        {
            float intro = Mathf.Max(0.01f, m_introTime);
            float outro = Mathf.Max(0.01f, m_outroTime);

            if (m_elapsed < intro)
            {
                float t = m_elapsed / intro;
                intensity = t * t;
                return Mathf.Clamp(EaseOutBack(t), 0.0f, 1.15f);
            }

            if (m_elapsed < intro + m_duration)
            {
                intensity = 1.0f;
                return 1.0f;
            }

            float u = Mathf.Clamp01((m_elapsed - intro - m_duration) / outro);
            intensity = 1.0f - u;
            return 1.0f - u * u;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1.0f;
            float x = t - 1.0f;
            return 1.0f + c3 * x * x * x + c1 * x * x;
        }

        private void FixedUpdate()
        {
            if (m_character == null) return;

            // 出現し終わってから、消え始めるまでだけ判定を出す
            float activeFrom = Mathf.Max(0.01f, m_introTime);
            float activeTo = activeFrom + m_duration;
            if (m_elapsed < activeFrom || m_elapsed > activeTo) return;

            Vector3 center = m_character.transform.position + Vector3.up * 0.5f;
            int count = Physics.OverlapSphereNonAlloc(center, m_knockRadius, m_overlaps, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var block = m_overlaps[i].GetComponentInParent<TumikiBlock>();
                if (block == null) continue;
                if (block.CurrentState != TumikiBlock.State.Flying) continue;

                Vector3 toBlock = block.transform.position - center;
                toBlock.y = 0.0f;
                if (toBlock.sqrMagnitude < 0.0001f) continue;

                // 近づいてくる積み木だけをはじき返す(自分の撃った弾は対象外)
                Vector3 toPlayer = -toBlock;
                if (Vector3.Dot(block.HorizontalVelocity, toPlayer.normalized) <= 0.1f) continue;

                float angle = Mathf.Atan2(toBlock.x, toBlock.z) * Mathf.Rad2Deg;
                if (!InAnySector(angle)) continue;

                if (m_lastKnockTimes.TryGetValue(block, out float last) && Time.time - last < m_knockInterval) continue;

                block.Launch(toBlock.normalized, m_knockSpeed, m_character);
                m_lastKnockTimes[block] = Time.time;
            }
        }

        bool InAnySector(float angle)
        {
            for (int i = 0; i < kSectorOffsets.Length; i++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(angle, m_baseYaw + kSectorOffsets[i])) <= m_sectorHalfAngle)
                {
                    return true;
                }
            }
            return false;
        }

        // 見た目を元に戻す(必殺技終了・キャラ消滅時)
        private void Cleanup()
        {
            RestorePlayerHammerMaterial();

            if (m_hammerMaterial != null)
            {
                Destroy(m_hammerMaterial);
                m_hammerMaterial = null;
            }
            if (m_trailMaterial != null)
            {
                Destroy(m_trailMaterial);
                m_trailMaterial = null;
            }
        }

        private void OnDestroy()
        {
            RestorePlayerHammerMaterial();
        }
    }
}
