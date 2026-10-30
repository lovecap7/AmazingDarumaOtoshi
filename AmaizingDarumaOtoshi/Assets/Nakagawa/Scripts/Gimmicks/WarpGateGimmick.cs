using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ワープゲート。地面に埋め込んだ円形のゲートに入ったものを、同じグループの自分以外のゲートへ送る
    //  ・行き先: ランダム / 順番。抽選はグループごとに独立
    //  ・対象: プレイヤーのみ / 積み木のみ / どちらも(幽霊状態のプレイヤーもプレイヤーとして送る)
    //  ・速さと向きは保つ。向きは「そのまま」か「ゲートの向きを基準に回す」を選べる
    //  ・飛んでいる積み木は攻撃判定と持ち主を保ったまま出てくる。積まれている積み木はプレイヤーと一緒に移動する
    //  ・ワープ範囲(中心からの半径)に入ったら送る。リングの半径とは別に決められる
    //  ・入ったものは止めておき(他の影響を受けない)、入口で縮みながら吸い込まれ、出口で元の大きさに戻ってから動き出す
    //  ・出口は出てくる直前に光る。入口・出口・ワープしているものにはエフェクトを出す(色はグループの色)
    //  ・出たものは、出口のゲートから離れて一定時間たつまで、そのゲートに反応しない
    //  ・すべての出口の上に何か乗っている場合はワープさせない
    // 見た目: リングの色はグループごと、リングの模様(顔マーク / 積み木マーク / 両方)で対象を区別する
    // このTransformの位置がゲートの中心(床の高さ)。向き(forward)を基準に回す方式で使う
    [DefaultExecutionOrder(-10)]
    public class WarpGateGimmick : MonoBehaviour
    {
        public enum TargetType { Players, Blocks, Both }
        public enum Selection { Random, Sequential }
        public enum Orientation
        {
            // 向き・進む方向はそのまま
            Keep,
            // 入口の向きに対する向きを、出口の向きに対しても同じにする
            RelativeToGate,
        }

        [Header("ゲート")]
        [SerializeField] float m_radius = 1.0f;
        [SerializeField] int m_group = 0;
        [SerializeField] TargetType m_target = TargetType.Both;
        [SerializeField] Selection m_selection = Selection.Random;
        [SerializeField] Orientation m_orientation = Orientation.Keep;

        [Header("ワープ範囲")]
        // 中心からこの半径に入ったらワープする(リングの半径とは別に決められる。縁をかすっただけでは送らないよう小さめにする)
        [SerializeField] float m_warpRadius = 0.7f;
        // 表面からこの高さまでにいるものを対象にする
        [SerializeField] float m_detectHeight = 1.0f;
        // 出口の上に何か乗っているとみなす半径(0 ならリングの半径)
        [SerializeField] float m_occupiedRadius = 0.0f;

        [Header("時間")]
        // 入ってから縮んで出口へ移るまでの時間(出口が光る時間)
        [SerializeField, Min(0.0f)] float m_exitDelay = 0.2f;
        // 出口で元の大きさに戻るまでの時間(この間も止まっている)
        [SerializeField, Min(0.0f)] float m_appearTime = 0.2f;
        // 出口から離れてから、そのゲートに再び反応するまでの時間
        [SerializeField, Min(0.0f)] float m_rearmTime = 0.3f;

        [Header("移動の演出")]
        // 入口で縮みきったときの大きさの割合
        [SerializeField, Range(0.0f, 1.0f)] float m_shrinkScale = 0.05f;
        // 出口で戻るとき、一瞬この割合まで大きくなってから元に戻る
        [SerializeField, Min(1.0f)] float m_appearOvershoot = 1.15f;
        // 縮みながらゲートの中心へ吸い込まれる
        [SerializeField] bool m_pullToCenter = true;

        [Header("エフェクト")]
        // エフェクトの色にグループの色を掛ける
        [SerializeField] bool m_tintEffects = true;
        // 入口(吸い込まれたとき)
        [SerializeField] GimmickBurstSettings m_enterEffect = new GimmickBurstSettings
        {
            Count = 24, Speed = new Vector2(1.5f, 3.5f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.25f, 0.45f),
            Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.8f, 0.0f, 0.0f), Stretch = true,
            Shockwave = true, ShockwaveRadius = 1.4f, ShockwaveTime = 0.3f,
        };
        // 出口(出てきたとき)
        [SerializeField] GimmickBurstSettings m_exitEffect = new GimmickBurstSettings
        {
            Count = 30, Speed = new Vector2(2.0f, 4.5f), Size = new Vector2(0.06f, 0.18f), Lifetime = new Vector2(0.3f, 0.55f),
            Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.5f, 0.0f, 0.0f), Stretch = true,
            Shockwave = true, ShockwaveRadius = 1.8f, ShockwaveTime = 0.35f,
        };
        // ワープしているもの(縮み始め・出てきたときに、そのものの位置に出す)
        [SerializeField] GimmickBurstSettings m_targetEffect = new GimmickBurstSettings
        {
            Count = 14, Speed = new Vector2(0.5f, 1.5f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.3f, 0.5f),
            Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0.0f, 0.0f), Gravity = -0.1f,
        };

        [Header("見た目")]
        // リング(色をグループの色にする。半径に合わせて拡大する)
        [SerializeField] Transform m_ring;
        [SerializeField] Renderer m_ringRenderer;
        // 内側の渦(回り続ける。出口として光るときに明るくなる)
        [SerializeField] Renderer m_swirl;
        // 対象の模様(リングの上に並べる。顔マーク・積み木マーク)
        [SerializeField] GameObject[] m_faceMarks;
        [SerializeField] GameObject[] m_blockMarks;
        // グループ番号ごとのリングの色(足りなければ繰り返す)
        [SerializeField] Color[] m_groupColors =
        {
            new Color(0.95f, 0.3f, 0.6f), new Color(0.3f, 0.6f, 1.0f), new Color(0.55f, 0.9f, 0.3f), new Color(0.75f, 0.45f, 1.0f),
        };
        [SerializeField] float m_swirlSpeed = 160.0f;
        [SerializeField, Range(0.0f, 1.0f)] float m_swirlAlpha = 0.55f;
        // 出口として光るときの色・大きさ
        [SerializeField] Color m_glowColor = Color.white;
        [SerializeField] float m_glowScale = 1.25f;
        // 入ったときに入口が光る時間
        [SerializeField] float m_enterFlashTime = 0.25f;

        [Header("音")]
        [SerializeField] AudioClip m_warpClip;
        [SerializeField, Range(0.0f, 1.0f)] float m_warpVolume = 0.5f;

        // 移動中のもの(止めておき、入口で縮んで出口で元の大きさに戻る)
        class Travel
        {
            public Object Key;
            public IGimmickAffectable Target;
            public DarumaGhost Ghost;
            public WarpGateGimmick To;
            public Transform Body;
            public Vector3 BaseScale;
            public Vector3 HoldPosition;
            public float Yaw;
            public float Timer;
            public bool Arrived;
        }

        static readonly List<WarpGateGimmick> s_gates = new List<WarpGateGimmick>();
        static readonly HashSet<Object> s_traveling = new HashSet<Object>();
        static readonly Dictionary<int, System.Random> s_random = new Dictionary<int, System.Random>();
        static readonly Dictionary<int, int> s_sequence = new Dictionary<int, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_gates.Clear();
            s_traveling.Clear();
            s_random.Clear();
            s_sequence.Clear();
        }

        readonly List<Travel> m_travels = new List<Travel>();
        // 反応しない相手と、離れてから反応するまでの残り時間
        readonly Dictionary<Object, float> m_ignore = new Dictionary<Object, float>();
        readonly List<Object> m_ignoreKeys = new List<Object>();
        readonly List<IGimmickAffectable> m_found = new List<IGimmickAffectable>();
        readonly List<WarpGateGimmick> m_candidates = new List<WarpGateGimmick>();
        int m_incoming;
        float m_glowTimer, m_glowDuration;
        float m_flashTimer;
        float m_swirlAngle;
        Vector3 m_swirlScale = Vector3.one;

        public int Group => m_group;
        public float Radius => m_radius;
        public TargetType Target => m_target;
        Color GroupColor => m_groupColors != null && m_groupColors.Length > 0
            ? m_groupColors[((m_group % m_groupColors.Length) + m_groupColors.Length) % m_groupColors.Length] : Color.white;

        private void Awake()
        {
            ApplyShape();
        }

        private void OnEnable() => s_gates.Add(this);

        private void OnDisable()
        {
            s_gates.Remove(this);
            // 移動中のものはその場で元に戻す
            foreach (var t in m_travels) Release(t);
            m_travels.Clear();
            m_ignore.Clear();
            m_incoming = 0;
            m_glowTimer = 0.0f;
        }

        private void OnValidate()
        {
            m_radius = Mathf.Max(0.2f, m_radius);
            m_warpRadius = Mathf.Max(0.05f, m_warpRadius);
            m_occupiedRadius = Mathf.Max(0.0f, m_occupiedRadius);
            ApplyShape();
        }

        private void FixedUpdate()
        {
            if (GimmickClock.IsHeld) return;
            DetectEntrants();
            UpdateIgnore(Time.fixedDeltaTime);
        }

        float OccupiedRadius => m_occupiedRadius > 0.0f ? m_occupiedRadius : m_radius;

        //========================================
        // 送る
        //========================================

        private void DetectEntrants()
        {
            Vector3 center = transform.position;
            Vector3 half = new Vector3(m_warpRadius, m_detectHeight * 0.5f, m_warpRadius);
            GimmickUtil.OverlapBox(center + Vector3.up * (m_detectHeight * 0.5f), half, Quaternion.identity, m_found);
            foreach (var target in m_found)
            {
                if (!Accepts(target is TumikiBlock)) continue;
                if (!IsInside(target.Rigidbody.position, m_warpRadius)) continue;
                TryStart(target as Object, target, null, target.Rigidbody.position);
            }

            if (!Accepts(false)) return;
            foreach (var ghost in DarumaGhost.Active)
            {
                if (ghost == null || ghost.IsSuspended || !IsInside(ghost.transform.position, m_warpRadius)) continue;
                TryStart(ghost, null, ghost, ghost.transform.position);
            }
        }

        private void TryStart(Object key, IGimmickAffectable target, DarumaGhost ghost, Vector3 position)
        {
            if (key == null || m_ignore.ContainsKey(key) || s_traveling.Contains(key)) return;
            WarpGateGimmick exit = PickExit(key, ghost == null && target is TumikiBlock);
            // 出口がすべてふさがっていたら送らない
            if (exit == null) return;

            float yaw = m_orientation == Orientation.RelativeToGate
                ? Mathf.DeltaAngle(transform.eulerAngles.y, exit.transform.eulerAngles.y) : 0.0f;
            Transform body = ghost != null ? ghost.transform : target.Rigidbody.transform;
            var travel = new Travel
            {
                Key = key, Target = target, Ghost = ghost, To = exit, Body = body, BaseScale = body.localScale,
                HoldPosition = position, Yaw = yaw,
            };
            // 移動している間は止めておく(他のギミック・攻撃の影響を受けず、速さ・向きはそのまま残る)
            SetSuspended(travel, true);
            s_traveling.Add(key);
            m_ignore[key] = m_rearmTime;
            exit.m_incoming++;
            exit.StartGlow(m_exitDelay);
            m_flashTimer = m_enterFlashTime;
            GimmickSound.Play(GimmickSound.Or(m_warpClip, GimmickSound.Warp), m_warpVolume);
            PlayEffect(m_enterEffect, transform.position, m_radius);
            PlayEffect(m_targetEffect, position, 1.0f);

            m_travels.Add(travel);
            if (m_exitDelay <= 0.0f) Arrive(travel);
        }

        // 入口で縮みながら中心へ吸い込まれ、出口へ移ったら元の大きさに戻ってから動き出す
        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = m_travels.Count - 1; i >= 0; i--)
            {
                var t = m_travels[i];
                if (!IsAlive(t) || t.To == null)
                {
                    Release(t);
                    m_travels.RemoveAt(i);
                    continue;
                }

                t.Timer += dt;
                if (!t.Arrived)
                {
                    float u = m_exitDelay > 0.0f ? Mathf.Clamp01(t.Timer / m_exitDelay) : 1.0f;
                    t.Body.localScale = t.BaseScale * Mathf.Lerp(1.0f, m_shrinkScale, u * u);
                    if (m_pullToCenter)
                    {
                        Vector3 center = transform.position;
                        center.y = t.HoldPosition.y;
                        Move(t, Vector3.Lerp(t.HoldPosition, center, Smooth(u)), 0.0f);
                    }
                    if (u >= 1.0f) Arrive(t);
                    continue;
                }

                float a = m_appearTime > 0.0f ? Mathf.Clamp01(t.Timer / m_appearTime) : 1.0f;
                t.Body.localScale = t.BaseScale * AppearScale(a);
                if (a < 1.0f) continue;
                Release(t);
                m_travels.RemoveAt(i);
            }

            UpdateVisuals(dt);
        }

        // 出口へ移す(縮んだまま。この後、元の大きさに戻る)
        private void Arrive(Travel t)
        {
            WarpGateGimmick exit = t.To;
            t.Arrived = true;
            t.Timer = 0.0f;
            Vector3 pos = exit.transform.position;
            // 足元(積み木は中心)の高さは入口と同じだけ床から上げる
            if (t.Ghost == null) pos.y += t.HoldPosition.y - transform.position.y;
            Move(t, pos, t.Yaw);
            t.Body.localScale = t.BaseScale * AppearScale(0.0f);
            exit.m_ignore[t.Key] = exit.m_rearmTime;
            exit.PlayEffect(exit.m_exitEffect, exit.transform.position, exit.m_radius);
            exit.PlayEffect(exit.m_targetEffect, pos, 1.0f);
        }

        // 元の大きさに戻して動き出させる(途中で取りやめるときもその場で戻す)
        private void Release(Travel t)
        {
            s_traveling.Remove(t.Key);
            if (t.To != null) t.To.m_incoming = Mathf.Max(0, t.To.m_incoming - 1);
            if (!IsAlive(t)) return;
            t.Body.localScale = t.BaseScale;
            SetSuspended(t, false);
        }

        private static bool IsAlive(Travel t)
        {
            return t.Ghost != null || (t.Target != null && GimmickUtil.IsAlive(t.Target));
        }

        private static void SetSuspended(Travel t, bool suspended)
        {
            if (t.Ghost != null) t.Ghost.SetSuspended(suspended);
            else t.Target.SetSuspended(suspended);
        }

        private static void Move(Travel t, Vector3 position, float yaw)
        {
            if (t.Ghost != null) t.Ghost.Teleport(position, yaw);
            else t.Target.Teleport(position, yaw);
        }

        // 出口で戻るときの大きさ(0 → 少し大きく → 元の大きさ)
        private float AppearScale(float a)
        {
            const float kPeak = 0.7f;
            if (a < kPeak) return Mathf.Lerp(m_shrinkScale, m_appearOvershoot, Smooth(a / kPeak));
            return Mathf.Lerp(m_appearOvershoot, 1.0f, Smooth((a - kPeak) / (1.0f - kPeak)));
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }

        private void PlayEffect(GimmickBurstSettings settings, Vector3 position, float scale)
        {
            GimmickBurst.Play(settings, position, Quaternion.identity, scale, m_tintEffects ? GroupColor : (Color?)null);
        }

        // 同じグループの自分以外で、相手を受け付けて、上に何も乗っていないゲート
        private WarpGateGimmick PickExit(Object key, bool isBlock)
        {
            m_candidates.Clear();
            foreach (var gate in s_gates)
            {
                if (gate == this || gate.m_group != m_group || !gate.isActiveAndEnabled) continue;
                if (!gate.Accepts(isBlock)) continue;
                m_candidates.Add(gate);
            }
            if (m_candidates.Count == 0) return null;
            // 同じ並びで選べるよう名前順にする
            m_candidates.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            int start;
            if (m_selection == Selection.Sequential)
            {
                s_sequence.TryGetValue(m_group, out start);
                s_sequence[m_group] = start + 1;
            }
            else
            {
                if (!s_random.TryGetValue(m_group, out var rng)) s_random[m_group] = rng = new System.Random(m_group * 7919 + 17);
                start = rng.Next(m_candidates.Count);
            }

            // 選んだゲートがふさがっていれば、次のゲートを順に試す
            for (int i = 0; i < m_candidates.Count; i++)
            {
                var gate = m_candidates[(start + i) % m_candidates.Count];
                if (!gate.IsOccupied(key)) return gate;
            }
            return null;
        }

        // 上に何か(プレイヤー・積み木・幽霊)が乗っているか、何かが向かってきている
        public bool IsOccupied(Object except)
        {
            if (m_incoming > 0) return true;
            float radius = OccupiedRadius;
            Vector3 center = transform.position;
            Vector3 half = new Vector3(radius, m_detectHeight * 0.5f, radius);
            GimmickUtil.OverlapBox(center + Vector3.up * (m_detectHeight * 0.5f), half, Quaternion.identity, m_found);
            foreach (var target in m_found)
            {
                if (target as Object != except && IsInside(target.Rigidbody.position, radius)) return true;
            }
            foreach (var ghost in DarumaGhost.Active)
            {
                if (ghost != null && ghost != except && IsInside(ghost.transform.position, radius)) return true;
            }
            return false;
        }

        private bool Accepts(bool isBlock)
        {
            return m_target == TargetType.Both || (isBlock ? m_target == TargetType.Blocks : m_target == TargetType.Players);
        }

        private bool IsInside(Vector3 position, float radius)
        {
            Vector3 d = position - transform.position;
            if (d.y < -0.5f || d.y > m_detectHeight) return false;
            d.y = 0.0f;
            return d.sqrMagnitude <= radius * radius;
        }

        // 出てきたもの・入ったものは、ゲートから離れて一定時間たつまで反応しない
        private void UpdateIgnore(float dt)
        {
            if (m_ignore.Count == 0) return;
            m_ignoreKeys.Clear();
            m_ignoreKeys.AddRange(m_ignore.Keys);
            foreach (var key in m_ignoreKeys)
            {
                if (key == null || !StillNear(key))
                {
                    float left = m_ignore[key] - dt;
                    if (key == null || left <= 0.0f) m_ignore.Remove(key);
                    else m_ignore[key] = left;
                }
                else m_ignore[key] = m_rearmTime;
            }
        }

        private bool StillNear(Object key)
        {
            if (s_traveling.Contains(key)) return true;
            Vector3 pos = key is DarumaGhost g ? g.transform.position
                : key is IGimmickAffectable t ? t.Rigidbody.position : transform.position;
            return IsInside(pos, m_radius);
        }

        //========================================
        // 見た目
        //========================================

        private void StartGlow(float duration)
        {
            m_glowDuration = Mathf.Max(0.05f, duration);
            m_glowTimer = m_glowDuration;
        }

        private void UpdateVisuals(float dt)
        {
            m_glowTimer = Mathf.Max(0.0f, m_glowTimer - dt);
            m_flashTimer = Mathf.Max(0.0f, m_flashTimer - dt);
            m_swirlAngle = Mathf.Repeat(m_swirlAngle + m_swirlSpeed * dt, 360.0f);

            float glow = m_glowTimer > 0.0f ? 1.0f - m_glowTimer / m_glowDuration : 0.0f;
            float flash = m_enterFlashTime > 0.0f ? m_flashTimer / m_enterFlashTime : 0.0f;
            float light = Mathf.Max(glow, flash);

            if (m_swirl != null)
            {
                m_swirl.transform.localRotation = Quaternion.Euler(0.0f, -m_swirlAngle, 0.0f);
                m_swirl.transform.localScale = m_swirlScale * Mathf.Lerp(1.0f, m_glowScale, glow);
                Color c = Color.Lerp(EffectMeshes.Brighten(GroupColor, 0.35f), m_glowColor, light);
                EffectMeshes.SetColor(m_swirl, GimmickPalette.WithAlpha(c, Mathf.Lerp(m_swirlAlpha, 1.0f, light)));
            }
        }

        // 半径・グループの色・対象の模様を合わせる(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            if (m_ring != null) m_ring.localScale = new Vector3(m_radius, m_radius, m_radius);
            if (m_ringRenderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                m_ringRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", GroupColor);
                m_ringRenderer.SetPropertyBlock(mpb);
            }
            if (m_swirl != null)
            {
                m_swirlScale = new Vector3(m_radius * 0.9f, 1.0f, m_radius * 0.9f);
                m_swirl.transform.localScale = m_swirlScale;
                EffectMeshes.SetColor(m_swirl, GimmickPalette.WithAlpha(EffectMeshes.Brighten(GroupColor, 0.35f), m_swirlAlpha));
            }
            // 顔マークと積み木マークを交互にリングの上に並べる(どちらもなら両方、片方なら片方だけ)
            ArrangeMarks(m_faceMarks, m_target != TargetType.Blocks, 0.0f);
            ArrangeMarks(m_blockMarks, m_target != TargetType.Players, 45.0f);
        }

        private void ArrangeMarks(GameObject[] marks, bool visible, float angleOffset)
        {
            if (marks == null) return;
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i] == null) continue;
                // OnValidate からも呼ぶので SetActive ではなく描画の有効・無効で切り替える
                foreach (var r in marks[i].GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
                float a = (angleOffset + i * 360.0f / marks.Length) * Mathf.Deg2Rad;
                marks[i].transform.localPosition = new Vector3(Mathf.Sin(a), 0.0f, Mathf.Cos(a)) * m_radius + Vector3.up * (m_radius * 0.18f);
            }
        }

        private void OnDrawGizmos()
        {
            Vector3 c = transform.position;
            Gizmos.color = GroupColor;
            DrawCircle(c, m_radius);
            Gizmos.DrawLine(c, c + transform.forward * m_radius);
        }

        // 選んでいるときは、ワープ範囲(白)と、出口の上に何か乗っているとみなす範囲(灰)も描く
        private void OnDrawGizmosSelected()
        {
            Vector3 c = transform.position;
            Gizmos.color = Color.white;
            DrawCircle(c, m_warpRadius);
            DrawCircle(c + Vector3.up * m_detectHeight, m_warpRadius);
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            DrawCircle(c, OccupiedRadius);
        }

        private static void DrawCircle(Vector3 center, float radius)
        {
            const int kSegments = 32;
            Vector3 prev = center + new Vector3(radius, 0, 0);
            for (int i = 1; i <= kSegments; i++)
            {
                float a = i * Mathf.PI * 2.0f / kSegments;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
