using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // 幽霊キャラクター。脱落したプレイヤーが操作する
    // ・浮いているので奈落の上も移動できる(ステージ範囲内に制限)。壁は通れない
    // ・ジャンプ・積み木を積む・股抜きショットはできない
    // ・当たり判定を持たないので、攻撃を受けず、生存者や積み木もすり抜ける
    // ・遅いハンマーで薙ぎ払いができ、生存者にダメージを与えると復活を要求する
    // ・出たときは魂が降ってくる演出(GhostAppearEffect)が終わるまで見えず、動かない(IsAppearing)
    [RequireComponent(typeof(HitStop))]
    public class DarumaGhost : MonoBehaviour, IDarumaAttacker
    {
        // 移動速度など(ScriptableObjectから毎回読む)。ハンマーの性能はハンマー側のHammerParams
        [SerializeField] GhostParams m_params;
        [SerializeField] float m_turnSpeed = 15.0f;
        // 壁との当たり判定に使う半径
        [SerializeField] float m_radius = 0.45f;
        [SerializeField] DarumaHammer m_hammer;
        [Header("壁との重なり")]
        // 壁(ReflectWall)と重なったとき(せり上がり塀が足元から上がってきたなど)、壁の外へ押し出す速さ(m/秒)
        [SerializeField, Min(0.1f)] float m_pushOutSpeed = 8.0f;
        // 壁と重ならない場所を探す範囲(m)。出るとき・ワープしたとき・押し出すときに使う
        [SerializeField, Min(0.1f)] float m_freeSearchRadius = 4.0f;
        [Header("出現の演出")]
        // 見た目の子オブジェクト(出現の演出で大きさを変える)
        [SerializeField] Transform m_visual;
        // 魂が飛んで降ってくる演出の値(未設定なら演出なしですぐ出る)
        [SerializeField] GhostAppearParams m_appearParams;

        IDarumaCommandSource m_commandSource;
        readonly List<Renderer> m_hiddenRenderers = new List<Renderer>();
        StageArea m_stageArea;
        HitStop m_hitStop;
        Vector3 m_moveDirection;
        bool m_reviveRequested;
        readonly RaycastHit[] m_wallHits = new RaycastHit[8];
        readonly Collider[] m_wallOverlaps = new Collider[16];
        // 壁の外へ押し出している最中か・押し出す先
        bool m_isPushingOut;
        Vector3 m_pushTarget;
        // 空いた場所が見つからなかったとき、次に探すまでの時間(毎フレーム探して重くならないように)
        float m_searchCooldown;

        // 壁と重なっているかを調べるときの余裕(m)。壁に沿って滑っているだけ(ほぼ接している)では重なりとみなさない
        const float kOverlapTolerance = 0.02f;
        // 空いた場所を探すとき、壁から離す余裕(m)
        const float kFreeSkin = 0.02f;
        // 空いた場所を探す刻み(m)。この距離ずつ外へ広げ、輪の上も同じ間隔で調べる
        const float kSearchStep = 0.1f;
        const float kSearchRetryTime = 0.5f;

        static readonly List<DarumaGhost> s_active = new List<DarumaGhost>();

        // 出ている幽霊(当たり判定を持たないので、ギミックはここから探す)
        public static IReadOnlyList<DarumaGhost> Active => s_active;

        // ドメインの再読み込みをしない設定でも、再生のたびに空にする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_active.Clear();

        public Collider Body => null;
        // CPU が操作を決めるための読み取り用(ハンマー・移動の速さ・壁との当たり判定の半径)
        public DarumaHammer Hammer => m_hammer;
        public float MoveSpeed => m_params != null ? m_params.MoveSpeed : 0.0f;
        public float Radius => m_radius;
        public float LocalTimeScale => Mathf.Max(0.01f, m_hitStop.TimeScale);

        private void Awake()
        {
            m_hitStop = GetComponent<HitStop>();
        }

        public Transform Visual => m_visual;
        public GhostAppearParams AppearParams => m_appearParams;

        // 出現の演出中(魂が降ってくるまで)。見えない・動かない・ギミックの対象にならない(Active に入らない)
        public bool IsAppearing { get; private set; }

        private void OnEnable()
        {
            if (!IsAppearing) s_active.Add(this);
        }

        private void OnDisable() => s_active.Remove(this);

        // 出現の演出を始める(GhostAppearEffect から呼ぶ)
        public void BeginAppear()
        {
            if (IsAppearing) return;
            IsAppearing = true;
            m_moveDirection = Vector3.zero;
            s_active.Remove(this);
            m_hiddenRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                m_hiddenRenderers.Add(r);
            }
        }

        // 出現の演出が終わった。ここから動かせる
        public void FinishAppear()
        {
            if (!IsAppearing) return;
            IsAppearing = false;
            foreach (var r in m_hiddenRenderers) if (r != null) r.enabled = true;
            m_hiddenRenderers.Clear();
            if (isActiveAndEnabled && !s_active.Contains(this)) s_active.Add(this);
            // 見えない間にせり上がり塀などが上がってきて重なっていたら、見える前に外へ出す
            SnapToFreePosition();
        }

        // 瞬間移動する(ワープなど)。浮いている高さはそのままで、ステージ範囲内に収める
        public void Teleport(Vector3 position, float yawDegrees)
        {
            position.y = transform.position.y;
            if (m_stageArea != null) position = m_stageArea.Clamp(position, m_radius);
            // 着いた先が壁の中なら、一番近い空いた場所にずらす
            if (TryFindFreePosition(position, out Vector3 free)) position = free;
            m_isPushingOut = false;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0.0f, yawDegrees, 0.0f) * transform.rotation;
        }

        public void StartHitStop(float duration, float timeScale, float shake)
        {
            m_hitStop.Begin(duration, timeScale, shake);
        }

        // 生存者にダメージを与えたので復活したい
        public event Action<DarumaGhost> ReviveRequested;

        public void Init(StageArea stageArea)
        {
            m_stageArea = stageArea;
            // やられた場所が壁の中(ステージの範囲の端は外壁の中・せり上がった塀の上でやられたなど)なら、一番近い空いた場所に出す
            SnapToFreePosition();
        }

        public void SetCommandSource(IDarumaCommandSource source)
        {
            m_commandSource = source;
            m_moveDirection = Vector3.zero;
        }

        // 一時的に止めている(ワープの移動中など)。止めている間は操作も移動もしない
        public bool IsSuspended { get; private set; }

        public void SetSuspended(bool suspended)
        {
            IsSuspended = suspended;
            if (suspended) m_moveDirection = Vector3.zero;
        }

        private void Update()
        {
            if (IsSuspended || IsAppearing) return;
            if (m_commandSource != null)
            {
                DarumaCommand command = m_commandSource.ReadCommand();
                Vector2 move = Vector2.ClampMagnitude(command.Move, 1.0f);
                m_moveDirection = new Vector3(move.x, 0.0f, move.y);
                if (command.Sweep) m_hammer.TrySweep();
            }

            // 壁と重なっている間は操作で動かさず、壁の外へ押し出す
            if (!UpdatePushOut()) UpdateMove();
        }

        //========================================
        // 壁と重ならないようにする
        // 壁の中にいると、壁に沿って滑る処理(SlideAlongWalls)がどの向きにも「距離 0 で壁に当たった」になり、二度と動けなくなるため
        //========================================

        // 壁と重なっていたら、一番近い空いた場所へすべるように押し出す。押し出している間は true
        private bool UpdatePushOut()
        {
            if (m_searchCooldown > 0.0f) m_searchCooldown -= Time.deltaTime;

            if (!m_isPushingOut || !IsFree(m_pushTarget))
            {
                m_isPushingOut = false;
                if (!OverlapsWall(transform.position, -kOverlapTolerance) || m_searchCooldown > 0.0f) return false;
                if (!TryFindFreePosition(transform.position, out m_pushTarget))
                {
                    m_searchCooldown = kSearchRetryTime;
                    return false;
                }
                m_isPushingOut = true;
            }

            float dt = Time.deltaTime * LocalTimeScale;
            transform.position = Vector3.MoveTowards(transform.position, m_pushTarget, m_pushOutSpeed * dt);
            if ((transform.position - m_pushTarget).sqrMagnitude < 0.000001f) m_isPushingOut = false;
            return true;
        }

        // 壁と重なっていたら、一番近い空いた場所へすぐ移す(見えていないときに使う)
        private void SnapToFreePosition()
        {
            m_isPushingOut = false;
            if (TryFindFreePosition(transform.position, out Vector3 free)) transform.position = free;
        }

        // pos から一番近い、壁と重ならずステージの範囲内の場所を探す(浮いている高さはそのまま)。見つからなければ false
        // 同じ距離に空いた場所がいくつかあるときは、ステージの中心に近いほうを選ぶ
        public bool TryFindFreePosition(Vector3 pos, out Vector3 result)
        {
            float y = pos.y;
            if (m_stageArea != null)
            {
                pos = m_stageArea.Clamp(pos, m_radius);
                pos.y = y;
            }
            result = pos;
            if (IsFree(pos)) return true;

            Vector3 center = m_stageArea != null ? m_stageArea.transform.position : pos;
            for (float d = kSearchStep; d <= m_freeSearchRadius + 0.0001f; d += kSearchStep)
            {
                int count = Mathf.Max(8, Mathf.CeilToInt(2.0f * Mathf.PI * d / kSearchStep));
                bool found = false;
                float best = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    float a = 2.0f * Mathf.PI * i / count;
                    Vector3 p = pos + new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a)) * d;
                    if (!IsFree(p)) continue;
                    float toCenter = new Vector2(p.x - center.x, p.z - center.z).sqrMagnitude;
                    if (toCenter >= best) continue;
                    best = toCenter;
                    result = p;
                    found = true;
                }
                if (found) return true;
            }
            result = pos;
            return false;
        }

        // ステージの範囲内で、壁から少し離れている
        private bool IsFree(Vector3 pos)
        {
            if (m_stageArea != null)
            {
                Vector3 c = m_stageArea.Clamp(pos, m_radius);
                if (Mathf.Abs(c.x - pos.x) > 0.001f || Mathf.Abs(c.z - pos.z) > 0.001f) return false;
            }
            return !OverlapsWall(pos, kFreeSkin);
        }

        // 壁(ReflectWall)と重なっているか。当たりの球は SlideAlongWalls と同じ(半径に margin を足す)
        private bool OverlapsWall(Vector3 pos, float margin)
        {
            Vector3 origin = pos + Vector3.up * (m_radius + 0.05f);
            int count = Physics.OverlapSphereNonAlloc(origin, m_radius + margin, m_wallOverlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (m_wallOverlaps[i].GetComponentInParent<ReflectWall>() != null) return true;
            }
            return false;
        }

        private void UpdateMove()
        {
            float dt = Time.deltaTime * LocalTimeScale;
            Vector3 delta = m_moveDirection * (m_params.MoveSpeed * dt);
            delta = SlideAlongWalls(delta);

            Vector3 pos = transform.position + delta;
            if (m_stageArea != null) pos = m_stageArea.Clamp(pos, m_radius);
            transform.position = pos;

            // ハンマーを振っている間は向きを固定
            if (m_moveDirection.sqrMagnitude > 0.0001f && !m_hammer.IsBusy)
            {
                Quaternion target = Quaternion.LookRotation(m_moveDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, m_turnSpeed * dt);
            }
        }

        // 壁(ReflectWall)にぶつかる移動は壁に沿って滑らせる
        private Vector3 SlideAlongWalls(Vector3 delta)
        {
            for (int iter = 0; iter < 2; iter++)
            {
                float dist = delta.magnitude;
                if (dist < 0.0001f) break;

                Vector3 dir = delta / dist;
                Vector3 origin = transform.position + Vector3.up * (m_radius + 0.05f);
                int count = Physics.SphereCastNonAlloc(origin, m_radius, dir, m_wallHits, dist, ~0, QueryTriggerInteraction.Ignore);

                float nearest = float.MaxValue;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = m_wallHits[i];
                    if (hit.collider.GetComponentInParent<ReflectWall>() == null) continue;
                    if (hit.distance < nearest)
                    {
                        nearest = hit.distance;
                        normal = hit.normal;
                    }
                }
                if (nearest == float.MaxValue) break;

                normal.y = 0.0f;
                normal.Normalize();
                // 壁の手前まで進み、残りは壁に沿った成分だけにする
                Vector3 toWall = dir * Mathf.Max(0.0f, nearest - 0.01f);
                Vector3 rest = delta - toWall;
                rest -= normal * Vector3.Dot(rest, normal);
                transform.position += toWall;
                delta = rest;
            }
            return delta;
        }

        public void OnDealtDamage()
        {
            if (m_reviveRequested) return;
            m_reviveRequested = true;
            ReviveRequested?.Invoke(this);
        }
    }
}
