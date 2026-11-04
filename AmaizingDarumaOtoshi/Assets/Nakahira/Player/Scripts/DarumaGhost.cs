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
        }

        // 瞬間移動する(ワープなど)。浮いている高さはそのままで、ステージ範囲内に収める
        public void Teleport(Vector3 position, float yawDegrees)
        {
            position.y = transform.position.y;
            if (m_stageArea != null) position = m_stageArea.Clamp(position, m_radius);
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

            UpdateMove();
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
