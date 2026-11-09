using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 傾き: 玉が入ったポケットの方へ台が傾く
    //  ・一定時間、台の上のプレイヤー・積み木・玉が、ポケットの方向へ押し流される(強さは始めと終わりでなめらかに変わる)
    //  ・床に流れる向きの矢印が並んで流れ、地鳴りが鳴り、台の見た目(m_tiltVisual)が少し傾く
    public class BilliardTiltGimmick : BilliardPocketGimmick
    {
        [Header("効果")]
        [SerializeField] float m_duration = 6.0f;
        [Tooltip("強さが上がる・下がる時間")]
        [SerializeField] float m_ramp = 0.6f;
        [Tooltip("プレイヤー・積み木を押す力(加速度 m/s²)")]
        [SerializeField] float m_acceleration = 16.0f;
        [Tooltip("玉を押す力(加速度 m/s²)")]
        [SerializeField] float m_ballAcceleration = 7.0f;
        [Tooltip("押す範囲の高さ")]
        [SerializeField] float m_height = 4.0f;

        [Header("見た目")]
        [Tooltip("傾ける見た目(当たり判定のない飾り。台の縁の木枠など)。未設定なら傾けない")]
        [SerializeField] Transform m_tiltVisual;
        [SerializeField] float m_tiltAngle = 2.5f;
        [Tooltip("床に並べる矢印の数(列 × 行)")]
        [SerializeField] Vector2Int m_arrowGrid = new Vector2Int(7, 4);
        [SerializeField] float m_arrowSize = 1.8f;
        [SerializeField] float m_arrowSpeed = 3.0f;
        [SerializeField] GimmickBurstSettings m_startEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.85f, 0.8f, 0.7f, 0.8f), ColorB = new Color(0.6f, 0.55f, 0.5f, 0.7f),
            Count = 30, Speed = new Vector2(1.0f, 3.0f), Size = new Vector2(0.2f, 0.45f), Lifetime = new Vector2(0.4f, 0.9f),
            Gravity = -0.05f, Shape = GimmickBurstShape.BoxEdge, ShapeSize = new Vector3(34.0f, 0, 17.0f),
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_volume = 0.4f;

        BilliardTable m_table;
        Vector3 m_dir;
        float m_time = -1.0f;
        Transform m_arrowRoot;
        readonly List<MeshRenderer> m_arrows = new List<MeshRenderer>();
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();
        AudioSource m_loop;
        Quaternion m_visualRotation = Quaternion.identity;
        static Mesh s_chevron;

        public override bool IsActive => m_time >= 0.0f;

        float Strength
        {
            get
            {
                if (m_time < 0.0f) return 0.0f;
                float up = m_ramp > 0.0f ? Mathf.Clamp01(m_time / m_ramp) : 1.0f;
                float down = m_ramp > 0.0f ? Mathf.Clamp01((m_duration - m_time) / m_ramp) : 1.0f;
                return Mathf.Min(up, down);
            }
        }

        private void Awake()
        {
            if (m_tiltVisual != null) m_visualRotation = m_tiltVisual.localRotation;
        }

        public override void Activate(BilliardTable table, Transform pocket, BilliardBall ball)
        {
            m_table = table;
            Vector3 to = (pocket != null ? pocket.position : ball.Position) - table.Center;
            to.y = 0.0f;
            m_dir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.right;
            m_time = 0.0f;
            BuildArrows();
            if (m_loop == null) m_loop = GimmickSound.CreateLoop(transform, GimmickSound.Rumble);
            var fx = m_startEffect.Clone();
            fx.ShapeSize = new Vector3(table.Size.x, 0.0f, table.Size.y);
            GimmickBurst.Play(fx, table.Center + Vector3.up * 0.1f);
            BilliardFx.Shake(0.3f, 0.5f);
        }

        public override void Stop()
        {
            m_time = -1.0f;
            if (m_arrowRoot != null) m_arrowRoot.gameObject.SetActive(false);
            GimmickSound.SetLoopVolume(m_loop, 0.0f);
            if (m_tiltVisual != null) m_tiltVisual.localRotation = m_visualRotation;
        }

        private void FixedUpdate()
        {
            if (!IsActive || m_table == null || GimmickClock.IsHeld || GimmickClock.IsNetClient) return;
            float s = Strength;
            if (s <= 0.0f) return;

            Vector3 c = m_table.Center;
            var half = new Vector3(m_table.Size.x * 0.5f + 1.0f, m_height * 0.5f, m_table.Size.y * 0.5f + 1.0f);
            GimmickUtil.OverlapBox(c + Vector3.up * (m_height * 0.5f), half, Quaternion.identity, m_targets);
            foreach (var t in m_targets) t.AddForce(m_dir * (m_acceleration * s));
            foreach (var b in m_table.AllBalls)
            {
                if (b.InPlay && !b.Rigidbody.isKinematic) b.Rigidbody.AddForce(m_dir * (m_ballAcceleration * s), ForceMode.Acceleration);
            }
        }

        private void Update()
        {
            if (!IsActive) return;
            if (!GimmickClock.IsHeld) m_time += Time.deltaTime;
            if (m_time >= m_duration)
            {
                Stop();
                return;
            }
            float s = Strength;
            GimmickSound.SetLoopVolume(m_loop, m_volume * s);
            UpdateArrows(s);

            // 見た目を傾ける(ポケットの側が下がる)
            if (m_tiltVisual != null)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, m_dir);
                m_tiltVisual.localRotation = Quaternion.AngleAxis(m_tiltAngle * s, axis) * m_visualRotation;
            }
        }

        void BuildArrows()
        {
            if (m_arrowRoot == null)
            {
                m_arrowRoot = new GameObject("TiltArrows").transform;
                m_arrowRoot.SetParent(transform, false);
                if (s_chevron == null) s_chevron = GimmickMeshes.BuildChevron(0.22f);
                int n = Mathf.Max(1, m_arrowGrid.x) * Mathf.Max(1, m_arrowGrid.y);
                for (int i = 0; i < n; i++) m_arrows.Add(EffectMeshes.Create($"Arrow_{i}", m_arrowRoot, s_chevron, true));
            }
            m_arrowRoot.gameObject.SetActive(true);
            m_arrowRoot.position = m_table.Center + Vector3.up * 0.04f;
            m_arrowRoot.rotation = Quaternion.LookRotation(m_dir);
        }

        // 矢印を流す向きに並べて流す。台からはみ出す矢印は消す
        void UpdateArrows(float s)
        {
            if (m_arrowRoot == null || m_table == null) return;
            int cols = Mathf.Max(1, m_arrowGrid.x), rows = Mathf.Max(1, m_arrowGrid.y);
            // 流れる向き(ローカル Z)の長さは、台を斜めに見たときの対角線ぶん
            float span = new Vector2(m_table.Size.x, m_table.Size.y).magnitude;
            float spacingZ = span / cols, spacingX = span / rows;
            float flow = Mathf.Repeat(m_time * m_arrowSpeed, spacingZ);
            Vector3 c = m_table.Center;
            Vector2 half = m_table.Size * 0.5f - Vector2.one * 0.8f;
            for (int i = 0; i < m_arrows.Count; i++)
            {
                int cx = i % cols, ry = i / cols;
                float z = -span * 0.5f + spacingZ * (cx + 0.5f) + flow;
                float x = -span * 0.5f + spacingX * (ry + 0.5f) + (cx % 2) * spacingX * 0.5f;
                var r = m_arrows[i];
                r.transform.localPosition = new Vector3(x, 0.0f, z);
                r.transform.localScale = Vector3.one * m_arrowSize;
                Vector3 w = r.transform.position - c;
                bool inside = Mathf.Abs(w.x) < half.x && Mathf.Abs(w.z) < half.y;
                // 流れの端で消えて、また出てくる
                float edge = Mathf.Clamp01(Mathf.Min(z + span * 0.5f, span * 0.5f - z) / (spacingZ * 0.8f));
                float blink = Mathf.Lerp(0.6f, 1.0f, GimmickPalette.Blink(m_time - cx * 0.08f, 2.5f));
                EffectMeshes.SetColor(r, GimmickPalette.WithAlpha(Color, inside ? 0.75f * s * edge * blink : 0.0f));
            }
        }
    }
}
