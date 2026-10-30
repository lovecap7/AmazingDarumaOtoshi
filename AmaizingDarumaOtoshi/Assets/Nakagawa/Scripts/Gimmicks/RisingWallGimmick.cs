using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // せり上がり塀。ふだんは床と見分けのつかないパネルが、予兆のあとにせり上がって壁になり、また床に戻る
    //  待機(床) → 予兆(縁が黄色く点滅・パネルが小刻みに揺れる・ゴトゴト音。当たり判定はまだ床のまま) → 上昇 → 壁 → 下降 → 待機 …
    //  ・壁の間は、飛んでいる積み木が壁と同じように跳ね返る(当たり判定に ReflectWall を付ける。幽霊も通れない)
    //  ・上に乗っているものは一緒に持ち上げる(高台になる)か、はじき出す
    //  ・開始のずれ(位相)を付けて並べると、波のように順番に立ち上がる
    // 床にいる間は当たり判定を消し、下の床に任せる(床どうしの継ぎ目に積み木が引っかからないように)
    // このTransformの位置が、床に戻っているときのパネルの上面の中心(床の高さ)
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Rigidbody))]
    public class RisingWallGimmick : MonoBehaviour
    {
        public enum Phase { Floor, Warning, Rising, Wall, Lowering }
        public enum RiderMode
        {
            // 一緒に持ち上げる(壁の上に乗った状態になる)
            Lift,
            // 上昇を始めたときに、パネルの外へはじき出す
            PushOff,
        }

        [Header("大きさ")]
        // パネルの大きさ(X, Z)
        [SerializeField] Vector2 m_size = new Vector2(2.0f, 1.5f);
        [SerializeField] float m_wallHeight = 1.5f;

        [Header("時間(秒)")]
        [SerializeField, Min(0.0f)] float m_floorTime = 3.0f;
        [SerializeField, Min(0.0f)] float m_warningTime = 1.0f;
        [SerializeField, Min(0.01f)] float m_riseTime = 0.5f;
        [SerializeField, Min(0.0f)] float m_wallTime = 3.0f;
        [SerializeField, Min(0.01f)] float m_lowerTime = 0.8f;
        // 開始のずれ。1周のうちこの時間だけ進んだところから始める(複数並べて波にする)
        [SerializeField] float m_phaseOffset = 0.0f;

        [Header("乗っているもの")]
        [SerializeField] RiderMode m_riderMode = RiderMode.Lift;
        // 表面からこの高さまでにいるものを乗っているとみなす
        [SerializeField] float m_rideHeight = 0.6f;
        // はじき出すときの水平の速さ・上向きの速さ
        [SerializeField] float m_pushPower = 8.0f;
        [SerializeField] float m_pushUpPower = 3.0f;

        [Header("見た目")]
        // パネルの本体(大きさを合わせる。上面がこのTransformの位置)
        [SerializeField] Transform m_panel;
        // 縁(予兆で点滅する)
        [SerializeField] Renderer[] m_edges;
        [SerializeField] Color m_edgeColor = new Color(0.35f, 0.33f, 0.3f, 1.0f);
        [SerializeField] Color m_warningColor = GimmickPalette.Warning;
        // 1秒あたりの点滅回数(予兆の終わりに向けて速くなる)
        [SerializeField] Vector2 m_blinkRate = new Vector2(3.0f, 8.0f);
        // 縁の幅
        [SerializeField] float m_edgeWidth = 0.08f;
        // 予兆の揺れの幅(m)・1秒あたりの揺れの回数
        [SerializeField] float m_shakeAmount = 0.03f;
        [SerializeField] float m_shakeFrequency = 30.0f;
        // 床とのちらつきを防ぐため、見た目だけ浮かせる高さ
        [SerializeField] float m_surfaceOffset = 0.004f;

        [Header("裏が見える透過")]
        // せり上がっている間は、ステージの壁と同じように裏の積み木・プレイヤーが見えるよう透過する(SeeThroughWall シェーダー)
        // 床に戻っている間は元の見た目に戻す(床が透けて穴にならないように)
        [SerializeField] bool m_seeThrough = true;
        // 未設定なら「Nakagawa/SeeThroughWall」
        [SerializeField] Shader m_seeThroughShader;
        // この高さ(m)より上がったら透過する
        [SerializeField, Min(0.0f)] float m_seeThroughMinHeight = 0.05f;

        [Header("エフェクト")]
        // 予兆の間、縁から黄色い粉が少しずつ噴き出す(この間隔ごと)
        [SerializeField, Min(0.02f)] float m_warningEffectInterval = 0.2f;
        [SerializeField] GimmickBurstSettings m_warningEffect = new GimmickBurstSettings
        {
            ColorA = GimmickPalette.Warning, ColorB = new Color(1.0f, 1.0f, 0.7f, 1.0f),
            Count = 6, Speed = new Vector2(0.4f, 1.2f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.3f, 0.6f),
            Gravity = -0.05f, Shape = GimmickBurstShape.BoxEdge, ShapeSize = new Vector3(1.0f, 0.0f, 1.0f),
        };
        // せり上がり始め: 縁から土ぼこりが舞い上がる
        [SerializeField] GimmickBurstSettings m_riseEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.85f, 0.8f, 0.7f, 0.9f), ColorB = new Color(0.65f, 0.6f, 0.52f, 0.8f),
            Count = 36, Speed = new Vector2(1.0f, 2.5f), Size = new Vector2(0.15f, 0.35f), Lifetime = new Vector2(0.4f, 0.8f),
            Gravity = -0.05f, Shape = GimmickBurstShape.BoxEdge, ShapeSize = new Vector3(1.0f, 0.0f, 1.0f),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.8f, 0.6f), ShockwaveRadius = 1.8f, ShockwaveTime = 0.3f,
        };
        // 下がり始め: 縁から土ぼこりが少し舞う
        [SerializeField] GimmickBurstSettings m_lowerEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.85f, 0.8f, 0.7f, 0.8f), ColorB = new Color(0.65f, 0.6f, 0.52f, 0.7f),
            Count = 20, Speed = new Vector2(0.5f, 1.5f), Size = new Vector2(0.12f, 0.28f), Lifetime = new Vector2(0.4f, 0.7f),
            Gravity = -0.03f, Shape = GimmickBurstShape.BoxEdge, ShapeSize = new Vector3(1.0f, 0.0f, 1.0f),
        };
        // 床に戻りきったとき: 縁から小さく舞う
        [SerializeField] GimmickBurstSettings m_landEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.85f, 0.8f, 0.7f, 0.8f), ColorB = new Color(0.65f, 0.6f, 0.52f, 0.7f),
            Count = 16, Speed = new Vector2(0.8f, 1.8f), Size = new Vector2(0.1f, 0.22f), Lifetime = new Vector2(0.3f, 0.5f),
            Shape = GimmickBurstShape.BoxEdge, ShapeSize = new Vector3(1.0f, 0.0f, 1.0f),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.8f, 0.4f), ShockwaveRadius = 1.4f, ShockwaveTime = 0.25f,
        };

        [Header("音")]
        // 未設定ならプログラムで作ったゴトゴト音
        [SerializeField] AudioClip m_rumbleClip;
        [SerializeField, Range(0.0f, 1.0f)] float m_rumbleVolume = 0.35f;

        Rigidbody m_rb;
        BoxCollider m_collider;
        Vector3 m_basePosition;
        float m_time;
        float m_height;
        Phase m_phase = Phase.Floor;
        AudioSource m_rumble;
        MaterialPropertyBlock m_mpb;
        float m_warningEffectTimer;
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();
        // 透過で差し替える見た目(パネルと縁)と、元のマテリアル・透過用のマテリアル
        Renderer[] m_seeThroughRenderers;
        Material[] m_normalMaterials, m_seeThroughMaterials;
        bool m_seeThroughOn;
        // 元のマテリアルごとの透過用マテリアル(同じ見た目のパネルどうしで共有する)
        static readonly Dictionary<Material, Material> s_seeThroughCache = new Dictionary<Material, Material>();

        public Phase CurrentPhase => m_phase;
        float Period => m_floorTime + m_warningTime + m_riseTime + m_wallTime + m_lowerTime;

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_collider = GetComponentInChildren<BoxCollider>(true);
            m_basePosition = transform.position;
            m_mpb = new MaterialPropertyBlock();
            m_rumble = GimmickSound.CreateLoop(transform, GimmickSound.Or(m_rumbleClip, GimmickSound.Rumble));
            ApplyShape();
            SetupSeeThrough();
        }

        // ステージの切り替えで有効になるたびに最初(開始のずれ)からやり直す
        private void OnEnable()
        {
            m_time = GimmickClock.StartTime(m_phaseOffset, Period);
            Evaluate(out m_phase, out m_height);
            if (m_rb != null)
            {
                m_rb.position = m_basePosition + Vector3.up * m_height;
                transform.position = m_rb.position;
            }
            SetSolid(m_phase != Phase.Floor && m_phase != Phase.Warning);
        }

        private void OnDisable()
        {
            GimmickSound.SetLoopVolume(m_rumble, 0.0f);
            SetSeeThrough(false);
        }

        private void OnValidate()
        {
            m_size = Vector2.Max(m_size, new Vector2(0.1f, 0.1f));
            m_wallHeight = Mathf.Max(0.1f, m_wallHeight);
            ApplyShape();
        }

        private void FixedUpdate()
        {
            if (GimmickClock.IsHeld) return;
            float dt = Time.fixedDeltaTime;
            m_time = GimmickClock.Step(m_time, dt, m_phaseOffset, Period);
            Phase before = m_phase;
            Evaluate(out m_phase, out float height);

            if (m_phase == Phase.Rising && before != Phase.Rising)
            {
                SetSolid(true);
                if (m_riderMode == RiderMode.PushOff) PushOffRiders();
                PlayEdgeEffect(m_riseEffect);
            }
            if (m_phase == Phase.Lowering && before != Phase.Lowering) PlayEdgeEffect(m_lowerEffect);
            if (m_phase == Phase.Floor && before == Phase.Lowering) PlayEdgeEffect(m_landEffect);
            if (m_phase == Phase.Floor || m_phase == Phase.Warning) SetSolid(false);

            float delta = height - m_height;
            m_height = height;
            m_rb.MovePosition(m_basePosition + Vector3.up * height);
            if (delta != 0.0f && m_riderMode == RiderMode.Lift) CarryRiders(delta / dt);
        }

        private void Update()
        {
            // 止めている間は予兆(点滅・揺れ・音・粉)も出さない
            bool warning = m_phase == Phase.Warning && !GimmickClock.IsHeld;
            float progress = WarningProgress();

            // 縁: 予兆の間は黄色く点滅(終わりに向けて速く)
            Color edge = m_edgeColor;
            if (warning)
            {
                float rate = Mathf.Lerp(m_blinkRate.x, m_blinkRate.y, progress);
                edge = Color.Lerp(m_edgeColor, m_warningColor, GimmickPalette.Blink(m_time, rate));
            }
            SetEdgeColor(edge);

            // パネル: 予兆の間は小刻みに揺れる(見た目だけ)
            if (m_panel != null)
            {
                Vector3 shake = Vector3.zero;
                if (warning)
                {
                    float t = Time.time * m_shakeFrequency;
                    shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, 0.0f, Mathf.PerlinNoise(0.7f, t) - 0.5f) * (m_shakeAmount * 2.0f);
                }
                m_panel.localPosition = new Vector3(shake.x, m_surfaceOffset - m_wallHeight * 0.5f, shake.z);
            }

            GimmickSound.SetLoopVolume(m_rumble, warning ? m_rumbleVolume : 0.0f);

            // 上がっている間だけ裏が見えるよう透過する(見た目の高さで切り替える)
            SetSeeThrough(m_seeThrough && transform.position.y - m_basePosition.y > m_seeThroughMinHeight);

            // 予兆の間は一定の間隔で縁から粉を噴き出す
            if (warning)
            {
                m_warningEffectTimer -= Time.deltaTime;
                if (m_warningEffectTimer <= 0.0f)
                {
                    m_warningEffectTimer = m_warningEffectInterval;
                    PlayEdgeEffect(m_warningEffect);
                }
            }
            else m_warningEffectTimer = 0.0f;
        }

        // 床の高さのパネルの縁に沿ってエフェクトを出す(形の大きさはパネルの大きさに合わせる)
        private void PlayEdgeEffect(GimmickBurstSettings settings)
        {
            if (settings == null || !settings.Enabled) return;
            var s = settings.Clone();
            s.ShapeSize = new Vector3(m_size.x * settings.ShapeSize.x, settings.ShapeSize.y, m_size.y * settings.ShapeSize.z);
            s.ShockwaveRadius = settings.ShockwaveRadius * Mathf.Max(m_size.x, m_size.y) * 0.5f;
            GimmickBurst.Play(s, m_basePosition + Vector3.up * 0.05f, transform.rotation);
        }

        // 1周の中の今の段階と高さ
        private void Evaluate(out Phase phase, out float height)
        {
            float period = Period;
            float t = period > 0.0f ? Mathf.Repeat(m_time, period) : 0.0f;
            height = 0.0f;
            phase = Phase.Floor;
            if ((t -= m_floorTime) < 0.0f) return;
            phase = Phase.Warning;
            if ((t -= m_warningTime) < 0.0f) return;
            phase = Phase.Rising;
            if ((t -= m_riseTime) < 0.0f)
            {
                height = m_wallHeight * Smooth(1.0f + t / m_riseTime);
                return;
            }
            phase = Phase.Wall;
            height = m_wallHeight;
            if ((t -= m_wallTime) < 0.0f) return;
            phase = Phase.Lowering;
            t -= m_lowerTime;
            height = m_wallHeight * (1.0f - Smooth(1.0f + Mathf.Min(0.0f, t) / m_lowerTime));
        }

        private float WarningProgress()
        {
            if (m_phase != Phase.Warning || m_warningTime <= 0.0f) return 0.0f;
            float t = Mathf.Repeat(m_time, Period) - m_floorTime;
            return Mathf.Clamp01(t / m_warningTime);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }

        private void SetSolid(bool solid)
        {
            if (m_collider != null && m_collider.enabled != solid) m_collider.enabled = solid;
        }

        // 上面に乗っているもの
        private void FindRiders()
        {
            Vector3 top = m_rb.position;
            Vector3 half = new Vector3(m_size.x * 0.5f, m_rideHeight * 0.5f, m_size.y * 0.5f);
            GimmickUtil.OverlapBox(top + Vector3.up * (m_rideHeight * 0.5f), half, transform.rotation, m_targets);
        }

        private void CarryRiders(float verticalSpeed)
        {
            FindRiders();
            foreach (var target in m_targets)
            {
                if (!GimmickUtil.MovesWithSolidPlatform(target)) target.AddCarryVelocity(Vector3.up * verticalSpeed);
            }
        }

        // パネルの中心から外へ、近い辺の向きにはじく
        private void PushOffRiders()
        {
            FindRiders();
            foreach (var target in m_targets)
            {
                Vector3 local = Quaternion.Inverse(transform.rotation) * (target.Rigidbody.position - m_rb.position);
                float nx = Mathf.Abs(local.x) / Mathf.Max(0.01f, m_size.x * 0.5f);
                float nz = Mathf.Abs(local.z) / Mathf.Max(0.01f, m_size.y * 0.5f);
                Vector3 dir = nx >= nz ? new Vector3(Mathf.Sign(local.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(local.z));
                target.AddKnockback(transform.rotation * dir * m_pushPower + Vector3.up * m_pushUpPower);
            }
        }

        // パネルと縁の、透過用のマテリアルを用意する(元の色・模様・つやを写す)
        private void SetupSeeThrough()
        {
            var shader = m_seeThroughShader != null ? m_seeThroughShader : Shader.Find("Nakagawa/SeeThroughWall");
            if (shader == null) return;
            var renderers = new List<Renderer>();
            if (m_panel != null && m_panel.TryGetComponent(out Renderer panel)) renderers.Add(panel);
            if (m_edges != null) foreach (var r in m_edges) if (r != null) renderers.Add(r);

            m_seeThroughRenderers = renderers.ToArray();
            m_normalMaterials = new Material[renderers.Count];
            m_seeThroughMaterials = new Material[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                var normal = renderers[i].sharedMaterial;
                m_normalMaterials[i] = normal;
                m_seeThroughMaterials[i] = SeeThroughMaterial(normal, shader);
            }
        }

        private static Material SeeThroughMaterial(Material normal, Shader shader)
        {
            if (normal == null) return null;
            if (normal.shader == shader) return normal;
            if (s_seeThroughCache.TryGetValue(normal, out var cached) && cached != null) return cached;
            var mat = new Material(shader) { name = normal.name + " (SeeThrough)" };
            if (normal.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", normal.GetColor("_BaseColor"));
            if (normal.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", normal.GetTexture("_BaseMap"));
                mat.SetTextureScale("_BaseMap", normal.GetTextureScale("_BaseMap"));
                mat.SetTextureOffset("_BaseMap", normal.GetTextureOffset("_BaseMap"));
            }
            if (normal.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", normal.GetFloat("_Smoothness"));
            if (normal.HasProperty("_Metallic")) mat.SetFloat("_Metallic", normal.GetFloat("_Metallic"));
            s_seeThroughCache[normal] = mat;
            return mat;
        }

        private void SetSeeThrough(bool on)
        {
            if (m_seeThroughOn == on || m_seeThroughRenderers == null) return;
            m_seeThroughOn = on;
            for (int i = 0; i < m_seeThroughRenderers.Length; i++)
            {
                var r = m_seeThroughRenderers[i];
                var mat = on ? m_seeThroughMaterials[i] : m_normalMaterials[i];
                if (r != null && mat != null) r.sharedMaterial = mat;
            }
        }

        private void SetEdgeColor(Color color)
        {
            if (m_edges == null) return;
            foreach (var r in m_edges)
            {
                if (r == null) continue;
                r.GetPropertyBlock(m_mpb);
                m_mpb.SetColor("_BaseColor", color);
                m_mpb.SetColor("_EmissionColor", color == m_edgeColor ? Color.black : color * 0.8f);
                r.SetPropertyBlock(m_mpb);
            }
        }

        // 大きさに合わせて当たり判定・パネル・縁を変える(エディタから設定を変えたときにも呼ぶ)
        // 本体の箱は上面からパネルの高さだけ下に伸びる(床に戻っているときは床の中に隠れる)
        public void ApplyShape()
        {
            var col = m_collider != null ? m_collider : GetComponentInChildren<BoxCollider>(true);
            if (col != null)
            {
                col.center = new Vector3(0.0f, -m_wallHeight * 0.5f, 0.0f);
                col.size = new Vector3(m_size.x, m_wallHeight, m_size.y);
            }
            if (m_panel != null)
            {
                m_panel.localScale = new Vector3(m_size.x, m_wallHeight, m_size.y);
                m_panel.localPosition = new Vector3(0.0f, m_surfaceOffset - m_wallHeight * 0.5f, 0.0f);
            }
            if (m_edges == null) return;
            // 縁は北・南・東・西の順の4本
            float hx = m_size.x * 0.5f, hz = m_size.y * 0.5f, w = m_edgeWidth, y = m_surfaceOffset * 2.0f;
            Vector3[] pos = { new Vector3(0, y, hz - w * 0.5f), new Vector3(0, y, -hz + w * 0.5f), new Vector3(hx - w * 0.5f, y, 0), new Vector3(-hx + w * 0.5f, y, 0) };
            Vector3[] scale = { new Vector3(m_size.x, 0.01f, w), new Vector3(m_size.x, 0.01f, w), new Vector3(w, 0.01f, m_size.y), new Vector3(w, 0.01f, m_size.y) };
            for (int i = 0; i < m_edges.Length && i < 4; i++)
            {
                if (m_edges[i] == null) continue;
                m_edges[i].transform.localPosition = pos[i];
                m_edges[i].transform.localScale = scale[i];
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1.0f, 0.9f, 0.2f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(Application.isPlaying ? m_basePosition : transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0, m_wallHeight * 0.5f, 0), new Vector3(m_size.x, m_wallHeight, m_size.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
