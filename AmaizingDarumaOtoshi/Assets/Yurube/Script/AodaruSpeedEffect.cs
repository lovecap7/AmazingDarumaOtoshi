using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あおだるの「移動速度UP」を目に見える形にする演出。
    // ・青い風の粒(走っている間、体のあとを流れる)
    // ・青い残像(速く動いている間、少し前の姿が残る)
    // どちらも青い積み木の数(=移動速度の上昇量)が多いほど派手になる。
    [RequireComponent(typeof(DarumaCharacter))]
    public class AodaruSpeedEffect : MonoBehaviour
    {
        [Tooltip("この速さ以上で動いているときだけ演出を出す(m/s)")]
        [SerializeField] float m_moveThreshold = 0.8f;
        [Tooltip("この数の青で演出が最大になる")]
        [SerializeField] float m_blueForMax = 6.0f;
        [Tooltip("風の粒の量(個/秒) 少ない→多い")]
        [SerializeField] float m_minWindRate = 115.0f;
        [SerializeField] float m_maxWindRate = 170.0f;
        [Tooltip("残像を出す間隔(秒) 遅い→速い")]
        [SerializeField] float m_minGhostInterval = 0.12f;
        [SerializeField] float m_maxGhostInterval = 0.05f;
        [Tooltip("残像が消えるまでの時間(秒)")]
        [SerializeField] float m_ghostLife = 0.25f;
        [Tooltip("何秒前の姿を残像にするか(0にすると今の姿と重なって点滅する)")]
        [SerializeField] float m_ghostDelay = 0.1f;
        [Tooltip("残像を出す最小の距離(m)。近すぎるとモデルと重なるので出さない")]
        [SerializeField] float m_minGhostDistance = 0.6f;

        DarumaCharacter m_character;
        MeshRenderer m_head;
        ParticleSystem m_wind;
        Material m_material;
        float m_ghostTimer;

        // 残像を「少し前の姿」にするための履歴
        struct PoseSample
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public float time;
        }

        readonly List<PoseSample> m_history = new List<PoseSample>(64);

        static Material s_fallbackMaterial;

        public void Init(Material material)
        {
            m_material = material;
        }

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();

            // 残像に使う「頭」のメッシュを探す
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "Head")
                {
                    m_head = renderer;
                    break;
                }
            }
        }

        private void Start()
        {
            CreateWind();
        }

        private void Update()
        {
            if (m_character == null) return;

            int blue = CountBlue();
            float t = Mathf.Clamp01(blue / Mathf.Max(0.01f, m_blueForMax));
            bool active = blue > 0 && m_character.CanBeAffected && HorizontalSpeed() >= m_moveThreshold;

            RecordPose();
            UpdateWind(active, t);
            UpdateGhosts(active, t);
        }

        // 残像用に、頭の位置・向き・大きさを毎フレーム記録しておく
        private void RecordPose()
        {
            if (m_head == null) return;

            m_history.Add(new PoseSample
            {
                position = m_head.transform.position,
                rotation = m_head.transform.rotation,
                scale = m_head.transform.lossyScale,
                time = Time.time
            });

            // 古すぎるものは捨てる
            float cutoff = Time.time - 0.5f;
            int remove = 0;
            while (remove < m_history.Count && m_history[remove].time < cutoff) remove++;
            if (remove > 0) m_history.RemoveRange(0, remove);
        }

        // 「m_ghostDelay 秒前」の姿を取り出す。まだ履歴が足りなければ false
        private bool TryGetDelayedPose(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            float target = Time.time - m_ghostDelay;
            for (int i = m_history.Count - 1; i >= 0; i--)
            {
                if (m_history[i].time > target) continue;
                position = m_history[i].position;
                rotation = m_history[i].rotation;
                scale = m_history[i].scale;
                return true;
            }
            return false;
        }

        private float HorizontalSpeed()
        {
            if (m_character.Rigidbody == null) return 0.0f;
            Vector3 v = m_character.Rigidbody.linearVelocity;
            v.y = 0.0f;
            return v.magnitude;
        }

        private int CountBlue()
        {
            var stack = m_character.Stack;
            return stack != null ? stack.CountColor(TumikiColor.Blue) : 0;
        }

        private static Material EffectMaterial
        {
            get
            {
                if (s_fallbackMaterial != null) return s_fallbackMaterial;

                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) return null;

                var material = new Material(shader);
                material.name = "AodaruWind (Runtime)";
                material.SetFloat("_Surface", 1.0f);
                material.SetFloat("_Blend", 2.0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_ZWrite", 0.0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                s_fallbackMaterial = material;
                return s_fallbackMaterial;
            }
        }

        private void CreateWind()
        {
            if (m_wind != null) return;

            var go = new GameObject("AodaruWind");
            go.transform.SetParent(transform, false);

            m_wind = go.AddComponent<ParticleSystem>();
            m_wind.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = m_wind.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = 0.0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.30f);
            main.startColor = new Color(0.45f, 0.80f, 1.0f, 0.9f);
            main.maxParticles = 200;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = m_wind.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;

            var shape = m_wind.shape;
            shape.enabled = false;

            // キャラクターの後ろ(-Z)へ流す
            // (x/y/z は同じカーブモードで揃えないと Unity がエラーを出す)
            var velocity = m_wind.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0.0f, 0.0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.0f, 0.0f);
            velocity.z = new ParticleSystem.MinMaxCurve(-3.0f, -1.0f);

            var colorOverLifetime = m_wind.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.60f, 0.90f, 1.0f), 0.0f),
                    new GradientColorKey(new Color(0.20f, 0.55f, 1.0f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.6f, 0.3f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = m_wind.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.3f));

            var renderer = m_wind.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var material = m_material != null ? m_material : EffectMaterial;
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            m_wind.Play();
        }

        private void UpdateWind(bool active, float t)
        {
            if (m_wind == null) return;

            var emission = m_wind.emission;
            emission.rateOverTime = active ? Mathf.Lerp(m_minWindRate, m_maxWindRate, t) : 0.0f;

            var main = m_wind.main;
            main.startSize = Mathf.Lerp(0.12f, 0.30f, t);
            main.startColor = Color.Lerp(new Color(0.45f, 0.80f, 1.0f, 0.7f), new Color(0.85f, 0.95f, 1.0f, 1.0f), t);
        }

        private void UpdateGhosts(bool active, float t)
        {
            if (!active)
            {
                m_ghostTimer = 0.0f;
                return;
            }

            m_ghostTimer -= Time.deltaTime;
            if (m_ghostTimer > 0.0f) return;

            m_ghostTimer = Mathf.Lerp(m_minGhostInterval, m_maxGhostInterval, t);
            SpawnGhost(t);
        }

        private void SpawnGhost(float t)
        {
            if (m_head == null) return;

            var headFilter = m_head.GetComponent<MeshFilter>();
            if (headFilter == null || headFilter.sharedMesh == null) return;

            var material = m_material != null ? m_material : EffectMaterial;
            if (material == null) return;

            // 「今の姿」ではなく少し前の姿に出す(モデルと重なって点滅するのを防ぐ)
            if (!TryGetDelayedPose(out Vector3 position, out Quaternion rotation, out Vector3 scale)) return;

            // 今のモデルと近すぎるなら出さない(重なって点滅して見えるため)
            if ((position - m_head.transform.position).sqrMagnitude < m_minGhostDistance * m_minGhostDistance) return;

            var go = new GameObject("AodaruGhost");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale * 0.95f;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = headFilter.sharedMesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var ghost = go.AddComponent<AodaruGhost>();
            ghost.Init(m_ghostLife,
                Color.Lerp(new Color(0.50f, 0.85f, 1.0f, 0.30f), new Color(0.85f, 0.95f, 1.0f, 0.55f), t));
        }
    }
}
