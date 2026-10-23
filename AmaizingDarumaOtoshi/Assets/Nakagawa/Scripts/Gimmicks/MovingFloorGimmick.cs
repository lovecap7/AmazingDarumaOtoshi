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
        int m_index;          // 今いる(または出発した)地点
        int m_direction = 1;  // 往復の向き
        float m_segmentTime;  // 区間を進んだ時間
        float m_waitTimer;
        bool m_stopped;

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
            m_index = 0;
            m_direction = 1;
            m_segmentTime = 0.0f;
            m_stopped = m_points.Count < 2;
            if (m_points.Count > 0)
            {
                m_rb.position = m_points[0];
                transform.position = m_points[0];
                m_waitTimer = WaitAt(0);
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
            if (m_stopped) return;
            float dt = Time.fixedDeltaTime;
            Vector3 before = m_rb.position;
            Vector3 after = Step(dt);
            m_rb.MovePosition(after);
            Carry(before, after, dt);
        }

        // このステップの移動先
        private Vector3 Step(float dt)
        {
            if (m_waitTimer > 0.0f)
            {
                m_waitTimer -= dt;
                return m_points[m_index];
            }

            int next = NextIndex();
            Vector3 from = m_points[m_index], to = m_points[next];
            float duration = Mathf.Max(0.01f, Vector3.Distance(from, to) / m_speed);
            m_segmentTime += dt;
            if (m_segmentTime < duration) return Vector3.Lerp(from, to, Ease(m_segmentTime / duration));

            // 次の地点に着いた
            Arrive(next);
            return to;
        }

        private void Arrive(int next)
        {
            if (m_loopMode == LoopMode.PingPong)
            {
                bool atEnd = next == m_points.Count - 1 || next == 0;
                if (atEnd) m_direction = -m_direction;
            }
            m_index = next;
            m_segmentTime = 0.0f;
            m_waitTimer = WaitAt(next);
            if (m_loopMode == LoopMode.OneWay && next == m_points.Count - 1) m_stopped = true;
        }

        private int NextIndex()
        {
            int n = m_points.Count;
            switch (m_loopMode)
            {
                case LoopMode.Loop: return (m_index + 1) % n;
                case LoopMode.PingPong: return Mathf.Clamp(m_index + m_direction, 0, n - 1);
                default: return Mathf.Min(m_index + 1, n - 1);
            }
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
