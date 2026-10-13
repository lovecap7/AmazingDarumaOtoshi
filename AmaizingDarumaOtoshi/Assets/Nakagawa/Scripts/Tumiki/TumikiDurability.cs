using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木の耐久度(積み木のプレハブに付ける)
    // 攻撃判定のある積み木が他の積み木に当たるたびに減り(TumikiDurabilityImpact)、0になると崩れる
    // プレイヤーに積まれる(上に乗って拾われる)と最大値まで回復する
    // 見た目は「最大値に対する残りの割合」で変わる(1回で壊れる設定なら、壊れる直前まで無傷に見える)
    //  ・削れた割合に応じて色が暗くなり、上面にヒビが増える
    [RequireComponent(typeof(TumikiBlock))]
    public class TumikiDurability : MonoBehaviour
    {
        // 新しく出る積み木の最大耐久値(0以下なら壊れない)。TumikiImpactModeToggle などが変える
        public static int DefaultMax { get; set; } = 1;

        // ドメインの再読み込みをしない設定でも、再生のたびに既定に戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DefaultMax = 1;

        [Tooltip("プレイヤーに積まれたら最大値まで回復する")]
        [SerializeField] bool m_refillOnStacked = true;

        [Header("見た目")]
        [Tooltip("ヒビの色(未設定ならヒビを出さない)")]
        [SerializeField] Material m_crackMaterial;
        [Tooltip("耐久値が0になる直前に出るヒビの数")]
        [SerializeField, Min(0)] int m_maxCracks = 6;
        [Tooltip("ヒビ1本の長さ(m)の範囲")]
        [SerializeField] Vector2 m_crackLength = new Vector2(0.3f, 0.45f);
        [Tooltip("ヒビの太さ(m)")]
        [SerializeField, Min(0.001f)] float m_crackWidth = 0.04f;
        [Tooltip("耐久値が0になる直前の色の暗さ(0で暗くならない)")]
        [SerializeField, Range(0.0f, 1.0f)] float m_maxDarken = 0.35f;

        static readonly int kBaseColorId = Shader.PropertyToID("_BaseColor");
        static Mesh s_cubeMesh;

        TumikiBlock m_block;
        Renderer m_renderer;
        MaterialPropertyBlock m_mpb;
        Transform m_crackRoot;
        readonly List<GameObject> m_cracks = new List<GameObject>();
        System.Random m_crackRandom;
        float m_appliedDarken = -1.0f;

        // 最大値(0以下なら壊れない)
        public int Max { get; private set; }
        public int Current { get; private set; }
        public bool IsUnbreakable => Max <= 0;
        // 残りの割合(壊れないなら1)
        public float Ratio => IsUnbreakable ? 1.0f : (float)Current / Max;
        // 削れた割合(見た目に使う)
        public float DamageRatio => 1.0f - Ratio;

        // 耐久値・最大値が変わった
        public event Action<TumikiDurability> Changed;

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            m_renderer = GetComponentInChildren<MeshRenderer>();
            m_mpb = new MaterialPropertyBlock();
            m_block.StateChanged += OnStateChanged;
            SetMax(DefaultMax, true);
        }

        private void OnDestroy()
        {
            if (m_block != null) m_block.StateChanged -= OnStateChanged;
        }

        // 最大値を変える。refill なら最大値まで回復する
        public void SetMax(int max, bool refill)
        {
            Max = max;
            Current = refill || IsUnbreakable ? Max : Mathf.Min(Current, Max);
            OnChanged();
        }

        public void Refill()
        {
            if (Current == Max) return;
            Current = Max;
            OnChanged();
        }

        // ダメージを受ける。耐久値がなくなったらtrue(壊れない積み木は常にfalse)
        public bool Damage(int amount = 1)
        {
            if (IsUnbreakable) return false;
            Current = Mathf.Max(0, Current - amount);
            OnChanged();
            return Current <= 0;
        }

        private void OnStateChanged(TumikiBlock block)
        {
            if (m_refillOnStacked && block.CurrentState == TumikiBlock.State.Stacked) Refill();
        }

        private void OnChanged()
        {
            UpdateCracks();
            Changed?.Invoke(this);
        }

        // 色は TumikiBlock が設定し直すことがある(色の変更など)ので、削れている間は毎フレーム暗くし直す
        private void LateUpdate()
        {
            if (m_renderer == null) return;
            float darken = m_maxDarken * DamageRatio;
            if (darken <= 0.0f && m_appliedDarken <= 0.0f) return;

            Color c = TumikiColorUtil.ToUnityColor(m_block.Color);
            c = Color.Lerp(c, Color.black, darken);
            m_renderer.GetPropertyBlock(m_mpb);
            m_mpb.SetColor(kBaseColorId, c);
            m_renderer.SetPropertyBlock(m_mpb);
            m_appliedDarken = darken;
        }

        // 削れた割合に応じた本数だけヒビを表示する(ヒビの位置は積み木ごとに固定)
        private void UpdateCracks()
        {
            if (m_crackMaterial == null || m_renderer == null) return;
            int count = DamageRatio <= 0.0f ? 0 : Mathf.CeilToInt(DamageRatio * m_maxCracks);
            if (count > 0) EnsureCracks(count);
            for (int i = 0; i < m_cracks.Count; i++) m_cracks[i].SetActive(i < count);
        }

        private void EnsureCracks(int count)
        {
            if (m_crackRoot == null)
            {
                // 見た目と一緒に震えるよう見た目の子にし、親の拡大縮小を打ち消して実寸で置けるようにする
                Transform visual = m_renderer.transform;
                m_crackRoot = new GameObject("Cracks").transform;
                m_crackRoot.SetParent(visual, false);
                Vector3 s = visual.lossyScale;
                m_crackRoot.localScale = new Vector3(1.0f / s.x, 1.0f / s.y, 1.0f / s.z);
            }
            if (s_cubeMesh == null) s_cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            // 積み木ごとに違う並びのヒビにする(作ったヒビは使い回すので、一度出たヒビの位置は変わらない)
            m_crackRandom ??= new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            var random = m_crackRandom;
            // ヒビが重ならないよう、向きを周りに散らす(少しずらす)
            float baseAngle = (float)random.NextDouble() * 360.0f;
            while (m_cracks.Count < count)
            {
                int index = m_cracks.Count;
                float angle = baseAngle + index * 360.0f / Mathf.Max(1, m_maxCracks) * 1.618f
                    + ((float)random.NextDouble() - 0.5f) * 30.0f;
                var crack = new GameObject($"Crack{index}");
                crack.transform.SetParent(m_crackRoot, false);
                BuildCrack(crack.transform, angle, random);
                m_cracks.Add(crack);
            }
        }

        // 上面を中心寄りから縁へ向かってジグザグに走るヒビ(折れ線の各区間を細い板で作る)
        // 試合のカメラは上から見下ろすので、上面に入れると目に入りやすい
        private void BuildCrack(Transform parent, float startAngle, System.Random random)
        {
            const int kSegments = 3;
            const float kSurfaceOffset = 0.006f;
            float limit = TumikiBlock.kRadius - m_crackWidth;
            float top = TumikiBlock.kHeight * 0.5f + kSurfaceOffset;
            float length = Mathf.Lerp(m_crackLength.x, m_crackLength.y, (float)random.NextDouble());
            float segment = length / kSegments;

            // 中心寄りの点から、外へ向かう向きで始める(上から見た向き。度)
            float heading = startAngle;
            float startRadius = (float)random.NextDouble() * TumikiBlock.kRadius * 0.3f;
            Vector2 p = Direction(startAngle + ((float)random.NextDouble() - 0.5f) * 60.0f) * startRadius;
            for (int i = 0; i < kSegments; i++)
            {
                heading += ((float)random.NextDouble() * 2.0f - 1.0f) * 40.0f;
                Vector2 next = p + Direction(heading) * segment;
                // 縁からはみ出す前に止める
                if (next.magnitude > limit) next = next.normalized * limit;
                Vector2 delta = next - p;
                if (delta.magnitude < 0.01f) break;

                Vector2 mid = (p + next) * 0.5f;
                var seg = new GameObject($"Segment{i}").transform;
                seg.SetParent(parent, false);
                seg.localPosition = new Vector3(mid.x, top, mid.y);
                // 板の長い辺(ローカルZ)を折れ線の向きに合わせる
                seg.localRotation = Quaternion.Euler(0.0f, Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 0.0f);
                // つなぎ目に隙間ができないよう少し長くする
                seg.localScale = new Vector3(m_crackWidth, 0.01f, delta.magnitude + m_crackWidth * 0.5f);
                seg.gameObject.AddComponent<MeshFilter>().sharedMesh = s_cubeMesh;
                var r = seg.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = m_crackMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p = next;
            }
        }

        // 上から見た向き(度。+Zが0、時計回り)の単位ベクトル(x, z)
        private static Vector2 Direction(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }
    }
}
