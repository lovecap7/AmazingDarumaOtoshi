using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 移動床(木のパレット)。指定した地点を順に移動し、各地点で待機時間だけ止まる
    //  回り方: ループ(A→B→C→A…) / 往復(A→B→C→B→A…) / 片道(最後の地点で止まる)
    //  動き出しと止まり際は加減速する(乗っているプレイヤーが置いていかれたように見えないように)
    //  乗っているものは、回転台と同じ仕組みで床の動きの分だけ一緒に運ぶ
    // 奈落の上を渡す足場として使う(上面が床と同じ高さ。当たり判定あり)
    // このTransformの位置が床の上面の中心。開始時は最初の地点にいる
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Rigidbody))]
    public class MovingFloorGimmick : MonoBehaviour
    {
        public enum LoopMode { Loop, PingPong, OneWay }

        [Serializable]
        public class Waypoint
        {
            // 移動床の子にしないこと(開始時の位置を使う)
            public Transform Point;
            // この地点での待機時間(負なら共通の待機時間)
            public float WaitTime = -1.0f;
        }

        [Header("移動")]
        [SerializeField] List<Waypoint> m_waypoints = new List<Waypoint>();
        [SerializeField] LoopMode m_loopMode = LoopMode.PingPong;
        // 移動の速さ(加減速を除いた平均の速さ m/s)
        [SerializeField] float m_speed = 2.5f;
        // 各地点での待機時間(全地点共通)
        [SerializeField, Min(0.0f)] float m_waitTime = 1.0f;
        // 加減速の強さ(0 = 一定の速さ / 1 = 動き出しと止まり際をなめらかに)
        [SerializeField, Range(0.0f, 1.0f)] float m_easing = 1.0f;

        [Header("大きさ")]
        // 床の大きさ(X, Z)。積み木3×3程度
        [SerializeField] Vector2 m_size = new Vector2(3.0f, 3.0f);
        [SerializeField] float m_thickness = 0.4f;
        // 表面からこの高さまでにいるものを乗っているとみなす
        [SerializeField] float m_rideHeight = 0.6f;

        [Header("見た目")]
        // パレットの見た目のまとめ(大きさに合わせて拡大する。1×1×1 の箱に収まる形で作っておく)
        [SerializeField] Transform m_visual;

        Rigidbody m_rb;
        BoxCollider m_collider;
        readonly List<Vector3> m_points = new List<Vector3>();
        readonly List<float> m_waits = new List<float>();
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();
        // 1周の区間(地点で待ってから次の地点へ動く)。位置は「始めてからの時間」から決める
        // (ネット対戦ではホストと時間をそろえる。GimmickClock)
        struct Leg
        {
            public int From, To;
            public float Wait, Move;
        }
        readonly List<Leg> m_legs = new List<Leg>();
        float m_cycle;        // 1周の時間(片道は最後まで行く時間)
        float m_time;
        bool m_stopped;

        // CPU が乗り降りを判断するための読み取り用
        // 地点の位置(開始時の位置)・回り方・速さ・大きさ・今の上面の中心
        public IReadOnlyList<Vector3> Points => m_points;
        public LoopMode Mode => m_loopMode;
        public float Speed => m_speed;
        public Vector2 Size => m_size;
        public Vector3 Position => m_rb != null ? m_rb.position : transform.position;
        // 地点で止まっているならその地点の番号、動いているなら -1
        public int StoppedAt
        {
            get
            {
                Evaluate(out int stoppedAt, out _, out _);
                return stoppedAt;
            }
        }
        // 止まっている残りの時間(動いていれば 0。片道の終点などで止まりきりなら無限)
        public float WaitRemaining
        {
            get
            {
                Evaluate(out _, out float remaining, out _);
                return remaining;
            }
        }
        // 次に止まる地点(止まっていれば、次に向かう地点)
        public int NextStop
        {
            get
            {
                Evaluate(out _, out _, out int next);
                return next;
            }
        }

        // 今の時間から、止まっている地点(動いていれば -1)・止まっている残りの時間・次に止まる地点を求める(CPU 用)
        private void Evaluate(out int stoppedAt, out float waitRemaining, out int nextStop)
        {
            stoppedAt = -1;
            waitRemaining = 0.0f;
            nextStop = 0;
            if (m_points.Count == 0) return;
            if (m_stopped || m_legs.Count == 0)
            {
                stoppedAt = 0;
                waitRemaining = float.PositiveInfinity;
                return;
            }
            float t = m_time;
            foreach (var leg in m_legs)
            {
                if (t < leg.Wait)
                {
                    stoppedAt = leg.From;
                    waitRemaining = leg.Wait - t;
                    nextStop = leg.To;
                    return;
                }
                t -= leg.Wait;
                if (t < leg.Move)
                {
                    nextStop = leg.To;
                    return;
                }
                t -= leg.Move;
            }
            // 片道の終点に着いて止まりきり
            int last = m_legs[m_legs.Count - 1].To;
            stoppedAt = last;
            waitRemaining = float.PositiveInfinity;
            nextStop = last;
        }
        // 繰り返す動きか(片道は最後の地点で止まるので繰り返さない)
        float Period => m_loopMode == LoopMode.OneWay ? 0.0f : m_cycle;

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_collider = GetComponent<BoxCollider>();
            foreach (var wp in m_waypoints)
            {
                if (wp == null || wp.Point == null) continue;
                m_points.Add(wp.Point.position);
                m_waits.Add(wp.WaitTime);
            }
            ApplyShape();
        }

        // ステージの切り替えで有効になるたびに最初の地点からやり直す
        private void OnEnable()
        {
            BuildLegs();
            m_stopped = m_points.Count < 2;
            m_time = m_stopped ? 0.0f : GimmickClock.StartTime(0.0f, Period);
            if (m_points.Count > 0)
            {
                Vector3 pos = m_stopped ? m_points[0] : PositionAt(m_time);
                m_rb.position = pos;
                transform.position = pos;
            }
        }

        private void OnValidate()
        {
            m_size = Vector2.Max(m_size, new Vector2(0.1f, 0.1f));
            m_thickness = Mathf.Max(0.05f, m_thickness);
            m_speed = Mathf.Max(0.01f, m_speed);
            ApplyShape();
        }

        private void FixedUpdate()
        {
            if (m_stopped || GimmickClock.IsHeld) return;
            float dt = Time.fixedDeltaTime;
            Vector3 before = m_rb.position;
            m_time = GimmickClock.Step(m_time, dt, 0.0f, Period);
            if (Period > 0.0f) m_time = Mathf.Repeat(m_time, Period);
            Vector3 after = PositionAt(m_time);
            m_rb.MovePosition(after);
            // ネット対戦で時計を合わせて一度に動いたときは運ばない(乗っているものが飛ばされないように)
            if ((after - before).magnitude <= m_speed * dt * 3.0f) Carry(before, after, dt);
        }

        // 1周の区間を作る。ループ: A→B→C→A / 往復: A→B→C→B→A / 片道: A→B→C(最後で止まる)。各地点で待ってから動く
        private void BuildLegs()
        {
            m_legs.Clear();
            m_cycle = 0.0f;
            int n = m_points.Count;
            if (n < 2) return;
            switch (m_loopMode)
            {
                case LoopMode.Loop:
                    for (int i = 0; i < n; i++) AddLeg(i, (i + 1) % n);
                    break;
                case LoopMode.PingPong:
                    for (int i = 0; i < n - 1; i++) AddLeg(i, i + 1);
                    for (int i = n - 1; i > 0; i--) AddLeg(i, i - 1);
                    break;
                default:
                    for (int i = 0; i < n - 1; i++) AddLeg(i, i + 1);
                    break;
            }
        }

        private void AddLeg(int from, int to)
        {
            var leg = new Leg
            {
                From = from,
                To = to,
                Wait = WaitAt(from),
                Move = Mathf.Max(0.01f, Vector3.Distance(m_points[from], m_points[to]) / m_speed),
            };
            m_legs.Add(leg);
            m_cycle += leg.Wait + leg.Move;
        }

        // 時間 t での位置(片道は最後まで行ったらそこで止まる)
        private Vector3 PositionAt(float t)
        {
            foreach (var leg in m_legs)
            {
                if (t < leg.Wait) return m_points[leg.From];
                t -= leg.Wait;
                if (t < leg.Move) return Vector3.Lerp(m_points[leg.From], m_points[leg.To], Ease(t / leg.Move));
                t -= leg.Move;
            }
            return m_legs.Count > 0 ? m_points[m_legs[m_legs.Count - 1].To] : m_points[0];
        }

        private float WaitAt(int index) => m_waits[index] >= 0.0f ? m_waits[index] : m_waitTime;

        // 加減速: 一定の速さとなめらかな動き(smootherstep)を強さで混ぜる
        private float Ease(float t)
        {
            float smooth = t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
            return Mathf.Lerp(t, smooth, m_easing);
        }

        // 乗っているものを床の動きの分だけ運ぶ
        private void Carry(Vector3 before, Vector3 after, float dt)
        {
            Vector3 delta = after - before;
            if (delta.sqrMagnitude < 1e-10f) return;
            Vector3 half = new Vector3(m_size.x * 0.5f, m_rideHeight * 0.5f, m_size.y * 0.5f);
            GimmickUtil.OverlapBox(before + Vector3.up * (m_rideHeight * 0.5f), half, transform.rotation, m_targets);
            foreach (var target in m_targets)
            {
                if (!GimmickUtil.MovesWithSolidPlatform(target)) target.AddCarryVelocity(delta / dt);
            }
        }

        // 大きさに合わせて当たり判定・見た目を変える(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            var col = m_collider != null ? m_collider : GetComponent<BoxCollider>();
            if (col != null)
            {
                col.center = new Vector3(0.0f, -m_thickness * 0.5f, 0.0f);
                col.size = new Vector3(m_size.x, m_thickness, m_size.y);
            }
            if (m_visual != null)
            {
                m_visual.localScale = new Vector3(m_size.x, m_thickness, m_size.y);
                m_visual.localPosition = new Vector3(0.0f, -m_thickness * 0.5f, 0.0f);
            }
        }

        private void OnDrawGizmos()
        {
            if (m_waypoints == null) return;
            Gizmos.color = new Color(0.9f, 0.7f, 0.4f, 0.9f);
            Transform prev = null, first = null;
            foreach (var wp in m_waypoints)
            {
                if (wp == null || wp.Point == null) continue;
                Gizmos.DrawWireCube(wp.Point.position + Vector3.down * (m_thickness * 0.5f), new Vector3(m_size.x, m_thickness, m_size.y));
                if (prev != null) Gizmos.DrawLine(prev.position, wp.Point.position);
                if (first == null) first = wp.Point;
                prev = wp.Point;
            }
            if (m_loopMode == LoopMode.Loop && first != null && prev != null) Gizmos.DrawLine(prev.position, first.position);
        }
    }
}
