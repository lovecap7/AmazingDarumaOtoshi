using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Nakagawa
{
    // ビリヤード台(Stage19)の進行役
    //  ・開始時(ステージが有効になったとき)に、手玉と的玉を最初の位置(シーンに置いた位置)に並べ直す
    //  ・一定間隔で、台の外から手とキュー(BilliardCue)が出てきて、狙ってから手玉を的玉に向かって突く
    //    狙いは「的玉をポケットへ落とせる向き」を優先して選ぶ(厚みが薄すぎる・途中に別の玉がある狙いは外す)
    //  ・的玉がポケットに入ったら、玉の種類(BilliardBall.Type)の番号のギミック(m_gimmicks)を発動し、一定時間後にその玉を復活させる
    //    手玉が入ったら(スクラッチ)、少し後に最初の位置へ戻す
    //  ・開始のカウントダウン中は止まり、試合が終わったら突かない。ネット対戦のクライアントでは何もしない
    public class BilliardTable : MonoBehaviour, INetGimmickState
    {
        [Header("台")]
        [Tooltip("台の中心(床の高さ)。未設定ならこの Transform")]
        [SerializeField] Transform m_center;
        [Tooltip("玉が転がる範囲(クッションの内側)の大きさ X・Z")]
        [SerializeField] Vector2 m_size = new Vector2(34.0f, 17.0f);
        [Tooltip("ポケット(床の穴)の中心。床の高さに置く")]
        [SerializeField] Transform[] m_pockets;
        [Tooltip("玉がこの高さより下に落ちたらポケットに入ったとみなす(床の高さから)")]
        [SerializeField] float m_pocketedDepth = 0.9f;

        [Header("玉")]
        [SerializeField] BilliardBall m_cueBall;
        [SerializeField] BilliardBall[] m_objectBalls;

        [Header("ギミック(玉の種類の番号順)")]
        [SerializeField] BilliardPocketGimmick[] m_gimmicks;

        [Header("ショット")]
        [SerializeField] BilliardCue m_cue;
        [Tooltip("開始(カウントダウンの終わり)から最初に突くまでの時間")]
        [SerializeField] float m_firstShotDelay = 4.0f;
        [Tooltip("キューが下がってから次に突き始めるまでの時間(最小・最大)")]
        [SerializeField] Vector2 m_shotInterval = new Vector2(7.0f, 11.0f);
        [Tooltip("すべての玉がこの速さより遅くなってから突く")]
        [SerializeField] float m_settleSpeed = 1.0f;
        [Tooltip("玉が止まるのを待つ最大の時間(過ぎたら転がっていても突く)")]
        [SerializeField] float m_maxSettleWait = 4.0f;
        [Tooltip("突く速さ(最小・最大)")]
        [SerializeField] Vector2 m_power = new Vector2(15.0f, 28.0f);
        [Tooltip("狙いの厚み(手玉の向きとポケットへの向きのなす角)の上限")]
        [SerializeField, Range(0.0f, 89.0f)] float m_maxCutAngle = 60.0f;
        [Tooltip("狙いのぶれ(度)")]
        [SerializeField] float m_aimError = 2.0f;

        [Header("復活")]
        [Tooltip("的玉がポケットに入ってから復活するまでの時間")]
        [SerializeField] float m_respawnDelay = 25.0f;
        [Tooltip("手玉がポケットに入ってから戻るまでの時間")]
        [SerializeField] float m_cueRespawnDelay = 3.0f;
        [Tooltip("復活の何秒前から、出てくる場所に印を出すか")]
        [SerializeField] float m_respawnWarning = 2.0f;

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_pocketEffect = new GimmickBurstSettings
        {
            ColorA = Color.white, ColorB = new Color(1.0f, 1.0f, 1.0f, 0.6f),
            Count = 34, Speed = new Vector2(2.5f, 7.0f), Size = new Vector2(0.08f, 0.22f), Lifetime = new Vector2(0.35f, 0.7f),
            Gravity = 0.6f, Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.8f, 0, 0), ConeAngle = 35.0f,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 1.0f, 1.0f, 0.9f), ShockwaveRadius = 3.2f, ShockwaveTime = 0.4f,
        };
        [SerializeField] GimmickBurstSettings m_respawnEffect = new GimmickBurstSettings
        {
            ColorA = Color.white, ColorB = new Color(1.0f, 1.0f, 0.85f, 1.0f),
            Count = 26, Speed = new Vector2(1.5f, 4.0f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.3f, 0.6f),
            Gravity = -0.2f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(1.0f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 1.0f, 1.0f, 0.8f), ShockwaveRadius = 2.0f, ShockwaveTime = 0.3f,
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_dropVolume = 0.6f;
        [SerializeField] string m_scratchLabel = "スクラッチ!";

        class Pending
        {
            public BilliardBall Ball;
            public float Timer;
            public Vector3 Spot;
            public bool HasSpot;
            public Transform Marker;
            public MeshRenderer Ring, Disc;
        }

        readonly Dictionary<BilliardBall, Vector3> m_home = new Dictionary<BilliardBall, Vector3>();
        readonly List<Pending> m_pending = new List<Pending>();
        readonly List<BilliardBall> m_allBalls = new List<BilliardBall>();
        readonly Collider[] m_overlap = new Collider[32];
        float m_shotTimer;
        float m_settleTimer;
        bool m_needsReset;
        bool m_cueWasBusy;

        // 的玉がポケットに入った・手玉が入った(入った玉、ポケット)。演出・拡張用
        public event Action<BilliardBall, Transform> BallPocketed;
        // 玉が復活した
        public event Action<BilliardBall> BallRespawned;
        // 手玉を突いた(向き、速さ)
        public event Action<Vector3, float> CueStruck;

        public Vector3 Center => m_center != null ? m_center.position : transform.position;
        public float FloorY => Center.y;
        public Vector2 Size => m_size;
        public float PocketedY => FloorY - m_pocketedDepth;
        public IReadOnlyList<Transform> Pockets => m_pockets ?? Array.Empty<Transform>();
        public BilliardBall CueBall => m_cueBall;
        public IReadOnlyList<BilliardBall> ObjectBalls => m_objectBalls ?? Array.Empty<BilliardBall>();
        public IReadOnlyList<BilliardBall> AllBalls => m_allBalls;
        public IReadOnlyList<BilliardPocketGimmick> Gimmicks => m_gimmicks ?? Array.Empty<BilliardPocketGimmick>();
        // 止めておく(カウントダウン中)
        public bool IsHeld => GimmickClock.IsHeld;

        private void Awake()
        {
            m_allBalls.Clear();
            if (m_cueBall != null) m_allBalls.Add(m_cueBall);
            if (m_objectBalls != null) foreach (var b in m_objectBalls) if (b != null) m_allBalls.Add(b);
            foreach (var b in m_allBalls)
            {
                b.Table = this;
                // シーンに置いた位置(床の上の点)を最初の位置として覚えておく
                Vector3 p = b.transform.position;
                m_home[b] = new Vector3(p.x, FloorY, p.z);
            }
        }

        private void OnEnable()
        {
            // 子の玉の準備(Awake)が終わってから並べ直す
            m_needsReset = true;
        }

        private void OnDisable()
        {
            ClearPending();
            if (m_cue != null) m_cue.Cancel(true);
            if (m_gimmicks != null) foreach (var g in m_gimmicks) if (g != null) g.Stop();
        }

        // 最初の状態に戻す(玉を最初の位置に並べ、復活待ち・ギミックを取り消す)
        public void ResetRack()
        {
            m_needsReset = false;
            ClearPending();
            if (m_cue != null) m_cue.Cancel(true);
            if (m_gimmicks != null) foreach (var g in m_gimmicks) if (g != null) g.Stop();
            foreach (var b in m_allBalls) b.PlaceAt(m_home[b], false);
            m_shotTimer = m_firstShotDelay;
            m_settleTimer = 0.0f;
            m_cueWasBusy = false;
        }

        private void Update()
        {
            if (m_needsReset) ResetRack();
            if (GimmickClock.IsNetClient) return;
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)

            float dt = IsHeld ? 0.0f : Time.deltaTime;
            UpdatePending(dt);
            CheckEscaped();

            var match = DarumaMatch.Current;
            bool finished = match != null && match.IsFinished;
            if (IsHeld || finished || m_cue == null) return;

            // キューが下がりきったら、次のショットまでの時間を数え始める
            bool busy = m_cue.IsBusy;
            if (m_cueWasBusy && !busy) m_shotTimer = Random.Range(m_shotInterval.x, m_shotInterval.y);
            m_cueWasBusy = busy;
            if (busy) return;

            m_shotTimer -= dt;
            if (m_shotTimer > 0.0f) return;
            TryShoot(dt);
        }

        //========================================
        // ショット
        //========================================

        void TryShoot(float dt)
        {
            if (m_cueBall == null || !m_cueBall.InPlay) return;
            // 玉が止まるのを少し待つ
            m_settleTimer += dt;
            if (m_settleTimer < m_maxSettleWait)
            {
                foreach (var b in m_allBalls)
                {
                    if (b.InPlay && b.Speed > m_settleSpeed) return;
                }
            }
            m_settleTimer = 0.0f;

            if (!ChooseShot(out Vector3 dir, out Vector3 aimPoint, out float power)) return;
            m_cue.Play(m_cueBall, dir, power, aimPoint, OnCueStrike);
            m_cueWasBusy = true;
        }

        // キューが手玉に当たった瞬間(BilliardCue から呼ばれる)
        void OnCueStrike(Vector3 dir, float power)
        {
            if (m_cueBall == null || !m_cueBall.InPlay) return;
            m_cueBall.Strike(dir * power);
            CueStruck?.Invoke(dir, power);
        }

        struct Candidate
        {
            public float Score;
            public Vector3 Dir, Aim;
            public float Power;
        }

        // 手玉で突く向きを選ぶ。的玉をポケットへ落とせる狙いを優先し、なければ一番近い的玉をまっすぐ狙う
        bool ChooseShot(out Vector3 dir, out Vector3 aimPoint, out float power)
        {
            dir = Vector3.forward;
            aimPoint = Vector3.zero;
            power = m_power.x;
            Vector3 cue = Flat(m_cueBall.Position);
            float r = m_cueBall.Radius;
            var list = new List<Candidate>();
            BilliardBall nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var ball in ObjectBalls)
            {
                if (ball == null || !ball.InPlay) continue;
                Vector3 tp = Flat(ball.Position);
                float direct = (tp - cue).magnitude;
                if (direct < nearestDist)
                {
                    nearestDist = direct;
                    nearest = ball;
                }
                foreach (var pocket in Pockets)
                {
                    if (pocket == null) continue;
                    Vector3 toPocket = Flat(pocket.position) - tp;
                    float dp = toPocket.magnitude;
                    if (dp < 0.1f) continue;
                    // 当てる点: 的玉の中心からポケットと反対側へ、玉2つ分の半径だけ離れた所に手玉の中心が来るように
                    Vector3 ghost = tp - toPocket / dp * (r + ball.Radius);
                    Vector3 aim = ghost - cue;
                    float da = aim.magnitude;
                    if (da < r * 2.0f) continue;
                    float cut = Vector3.Angle(aim, toPocket);
                    if (cut > m_maxCutAngle) continue;
                    if (!PathClear(cue, aim / da, da, r, ball)) continue;
                    if (!PathClear(tp, toPocket / dp, dp, ball.Radius, null)) continue;
                    float p = Mathf.Clamp(9.0f + (da + dp) * 0.55f + cut * 0.08f, m_power.x, m_power.y);
                    list.Add(new Candidate { Score = cut * 0.6f + da + dp * 0.7f, Dir = aim / da, Aim = ghost, Power = p });
                }
            }

            if (list.Count > 0)
            {
                list.Sort((a, b) => a.Score.CompareTo(b.Score));
                var pick = list[Random.Range(0, Mathf.Min(3, list.Count))];
                dir = pick.Dir;
                aimPoint = pick.Aim;
                power = pick.Power;
            }
            else if (nearest != null)
            {
                Vector3 tp = Flat(nearest.Position);
                dir = (tp - cue).normalized;
                aimPoint = tp - dir * (r + nearest.Radius);
                power = Random.Range(m_power.x, m_power.y);
            }
            else
            {
                // 的玉がすべて落ちている: 台の中心あたりへ突く(プレイヤーを狙う形になる)
                Vector3 to = Flat(Center) - cue + new Vector3(Random.Range(-4.0f, 4.0f), 0.0f, Random.Range(-3.0f, 3.0f));
                if (to.sqrMagnitude < 1.0f) to = Vector3.right;
                dir = to.normalized;
                aimPoint = cue + dir * Mathf.Min(8.0f, to.magnitude);
                power = Random.Range(m_power.x, m_power.y);
            }

            dir = Quaternion.Euler(0.0f, Random.Range(-m_aimError, m_aimError), 0.0f) * dir;
            aimPoint.y = FloorY;
            return true;
        }

        // from から dir へ distance 進む間に、ignore 以外の玉に当たらないか
        bool PathClear(Vector3 from, Vector3 dir, float distance, float radius, BilliardBall ignore)
        {
            Vector3 origin = new Vector3(from.x, FloorY + radius, from.z);
            var hits = Physics.SphereCastAll(origin, radius * 0.9f, dir, Mathf.Max(0.0f, distance - radius * 0.5f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                var b = h.collider.GetComponentInParent<BilliardBall>();
                if (b == null || b == ignore || !b.InPlay) continue;
                if ((Flat(b.Position) - from).sqrMagnitude < 0.01f) continue;   // 自分自身
                return false;
            }
            return true;
        }

        //========================================
        // ポケット
        //========================================

        // 玉が床より下に落ちた(BilliardBall から呼ばれる)
        public void NotifyFell(BilliardBall ball)
        {
            if (ball == null || !ball.InPlay) return;
            Transform pocket = NearestPocket(ball.Position);
            Vector3 pocketPos = pocket != null ? pocket.position : new Vector3(ball.Position.x, FloorY, ball.Position.z);
            ball.Sink(pocketPos, 0.3f);

            // ポケットから玉の色の粒が噴き上がる
            var fx = m_pocketEffect.Clone();
            fx.ColorA = ball.Color;
            fx.ColorB = EffectMeshes.Brighten(ball.Color, 0.5f);
            fx.ShockwaveColor = GimmickPalette.WithAlpha(ball.Color, 0.9f);
            GimmickBurst.Play(fx, pocketPos + Vector3.up * 0.1f, Quaternion.LookRotation(Vector3.up));
            GimmickSound.Play(BilliardFx.Drop, m_dropVolume);
            BilliardFx.Shake(0.18f, 0.25f);

            if (ball.IsCue)
            {
                BilliardFx.Popup(pocketPos + Vector3.up * 2.0f, m_scratchLabel, Color.white, 1.0f);
                if (m_cue != null) m_cue.Cancel(false);
                AddPending(ball, m_cueRespawnDelay);
            }
            else
            {
                BilliardPocketGimmick gimmick = GimmickFor(ball);
                if (gimmick != null)
                {
                    BilliardFx.Popup(pocketPos + Vector3.up * 2.2f, gimmick.Label, EffectMeshes.Brighten(gimmick.Color, 0.25f), 1.3f, 2.0f);
                    gimmick.Activate(this, pocket, ball);
                }
                AddPending(ball, m_respawnDelay);
            }
            BallPocketed?.Invoke(ball, pocket);
        }

        public BilliardPocketGimmick GimmickFor(BilliardBall ball)
        {
            if (ball == null || m_gimmicks == null || m_gimmicks.Length == 0) return null;
            return m_gimmicks[ball.Type % m_gimmicks.Length];
        }

        public Transform NearestPocket(Vector3 position)
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (var p in Pockets)
            {
                if (p == null) continue;
                float d = (Flat(p.position) - Flat(position)).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        // 台の外へ飛び出した玉もポケットに入ったものとして扱う
        void CheckEscaped()
        {
            Vector3 c = Center;
            foreach (var b in m_allBalls)
            {
                if (!b.InPlay) continue;
                Vector3 p = b.Position;
                if (Mathf.Abs(p.x - c.x) > m_size.x * 0.5f + 3.0f || Mathf.Abs(p.z - c.z) > m_size.y * 0.5f + 3.0f || p.y > FloorY + 8.0f)
                {
                    NotifyFell(b);
                }
            }
        }

        //========================================
        // 復活
        //========================================

        void AddPending(BilliardBall ball, float delay)
        {
            m_pending.RemoveAll(p => p.Ball == ball);
            m_pending.Add(new Pending { Ball = ball, Timer = delay });
        }

        void UpdatePending(float dt)
        {
            for (int i = m_pending.Count - 1; i >= 0; i--)
            {
                var p = m_pending[i];
                p.Timer -= dt;
                // 出てくる場所を決めて印を出す
                if (!p.HasSpot && p.Timer <= m_respawnWarning)
                {
                    p.Spot = FindFreeSpot(m_home[p.Ball], p.Ball.Radius);
                    p.HasSpot = true;
                    CreateMarker(p);
                }
                if (p.Marker != null) UpdateMarker(p);
                if (p.Timer > 0.0f) continue;

                // 印を出した後に誰かが乗っていたら、空いている場所を探し直す
                if (!IsFree(p.Spot, p.Ball.Radius)) p.Spot = FindFreeSpot(p.Spot, p.Ball.Radius);
                DestroyMarker(p);
                m_pending.RemoveAt(i);
                p.Ball.PlaceAt(p.Spot, true);
                var fx = m_respawnEffect.Clone();
                fx.ColorB = p.Ball.Color;
                fx.ShockwaveColor = GimmickPalette.WithAlpha(EffectMeshes.Brighten(p.Ball.Color, 0.4f), 0.8f);
                GimmickBurst.Play(fx, p.Spot + Vector3.up * 0.05f);
                GimmickSound.Play(GimmickSound.Pop, 0.45f, 0.8f);
                BallRespawned?.Invoke(p.Ball);
            }
        }

        void ClearPending()
        {
            foreach (var p in m_pending) DestroyMarker(p);
            m_pending.Clear();
        }

        void CreateMarker(Pending p)
        {
            var root = new GameObject("BilliardRespawnMarker").transform;
            root.SetParent(transform, true);
            root.position = p.Spot + Vector3.up * 0.03f;
            p.Marker = root;
            p.Ring = EffectMeshes.Create("Ring", root, EffectMeshes.Ring, true);
            p.Disc = EffectMeshes.Create("Disc", root, EffectMeshes.Disc, false);
            p.Ring.transform.localScale = Vector3.one * (p.Ball.Radius * 1.4f);
            p.Disc.transform.localScale = Vector3.one * p.Ball.Radius;
        }

        // 印: 玉の色の輪が縮みながら点滅し、出る直前ほど速く光る
        void UpdateMarker(Pending p)
        {
            float t = MarkerProgress(p);
            float rate = Mathf.Lerp(2.0f, 9.0f, t);
            float blink = GimmickPalette.Blink(Time.time, rate);
            float r = p.Ball.Radius;
            p.Ring.transform.localScale = Vector3.one * (r * Mathf.Lerp(2.2f, 1.1f, t));
            EffectMeshes.SetColor(p.Ring, GimmickPalette.WithAlpha(EffectMeshes.Brighten(p.Ball.Color, 0.3f), Mathf.Lerp(0.5f, 1.0f, blink)));
            EffectMeshes.SetColor(p.Disc, GimmickPalette.WithAlpha(p.Ball.Color, Mathf.Lerp(0.15f, 0.45f, t) * Mathf.Lerp(0.6f, 1.0f, blink)));
        }

        void DestroyMarker(Pending p)
        {
            if (p.Marker != null) Destroy(p.Marker.gameObject);
            p.Marker = null;
        }

        float MarkerProgress(Pending p) => m_respawnWarning > 0.0f ? 1.0f - Mathf.Clamp01(p.Timer / m_respawnWarning) : 1.0f;

        //========================================
        // ネット対戦用(suehara): 玉が出てくる場所の印をホストから受け取る(GimmickNet)
        // 玉・キューはそれぞれが受け取る。クライアントでは m_pending を印を出すためだけに使う
        //========================================

        public void WriteNetState(GimmickNetState s)
        {
            foreach (var b in m_allBalls)
            {
                var p = m_pending.Find(x => x.Ball == b);
                bool marker = p != null && p.Marker != null;
                s.AddBool(marker);
                s.AddVec3(marker ? p.Spot : Vector3.zero);
                s.Add(marker ? MarkerProgress(p) : 0.0f);
            }
        }

        public void ReadNetState(GimmickNetState s)
        {
            foreach (var b in m_allBalls)
            {
                bool marker = s.Bool();
                Vector3 spot = s.Vec3();
                float t = s.Float();
                if (!s.Ok) return;

                var p = m_pending.Find(x => x.Ball == b);
                if (!marker)
                {
                    if (p != null)
                    {
                        DestroyMarker(p);
                        m_pending.Remove(p);
                    }
                    continue;
                }
                if (p == null)
                {
                    p = new Pending { Ball = b };
                    m_pending.Add(p);
                }
                p.Spot = spot;
                p.HasSpot = true;
                p.Timer = (1.0f - t) * m_respawnWarning;
                if (p.Marker == null) CreateMarker(p);
                p.Marker.position = spot + Vector3.up * 0.03f;
                UpdateMarker(p);
            }
        }

        //========================================
        // 空いている場所
        //========================================

        // preferred が空いていればそこ、ふさがっていれば台の上の空いている場所(床の上の点)
        public Vector3 FindFreeSpot(Vector3 preferred, float radius)
        {
            preferred.y = FloorY;
            if (IsFree(preferred, radius)) return preferred;
            // まず近くを探し、だめなら台全体から探す
            for (int i = 0; i < 40; i++)
            {
                Vector3 p;
                if (i < 20)
                {
                    Vector2 o = Random.insideUnitCircle * (2.0f + i * 0.4f);
                    p = preferred + new Vector3(o.x, 0.0f, o.y);
                }
                else
                {
                    p = RandomPointOnTable(radius + 1.5f);
                }
                if (IsFree(p, radius)) return p;
            }
            return preferred;
        }

        // 台の上(クッションから margin 以上内側)のランダムな点
        public Vector3 RandomPointOnTable(float margin)
        {
            Vector3 c = Center;
            float hx = Mathf.Max(0.0f, m_size.x * 0.5f - margin);
            float hz = Mathf.Max(0.0f, m_size.y * 0.5f - margin);
            return new Vector3(c.x + Random.Range(-hx, hx), FloorY, c.z + Random.Range(-hz, hz));
        }

        // 玉を置ける場所か: 床があり(ポケットの穴の上ではない)、玉・プレイヤー・積み木・壁と重ならない
        public bool IsFree(Vector3 floorPoint, float radius)
        {
            Vector3 c = Center;
            if (Mathf.Abs(floorPoint.x - c.x) > m_size.x * 0.5f - radius || Mathf.Abs(floorPoint.z - c.z) > m_size.y * 0.5f - radius) return false;
            // 穴の近くは避ける
            foreach (var pocket in Pockets)
            {
                if (pocket != null && (Flat(pocket.position) - Flat(floorPoint)).magnitude < radius + 2.2f) return false;
            }
            // 足元に床があるか(中心と周り4点)
            for (int i = 0; i < 5; i++)
            {
                Vector3 o = i == 0 ? Vector3.zero : Quaternion.Euler(0, i * 90.0f, 0) * Vector3.forward * (radius * 0.8f);
                if (!Physics.Raycast(floorPoint + o + Vector3.up * 0.5f, Vector3.down, 1.0f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            }
            Vector3 bottom = floorPoint + Vector3.up * (radius + 0.1f);
            Vector3 top = floorPoint + Vector3.up * (radius + 1.5f);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius * 1.1f, m_overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var b = m_overlap[i].GetComponentInParent<BilliardBall>();
                if (b != null && !b.InPlay) continue;
                return false;
            }
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0.0f, v.z);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1.0f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(Center + Vector3.up * 0.05f, new Vector3(m_size.x, 0.1f, m_size.y));
            if (m_pockets == null) return;
            Gizmos.color = Color.black;
            foreach (var p in m_pockets) if (p != null) Gizmos.DrawWireSphere(p.position, 1.2f);
        }
    }
}
