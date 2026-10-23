using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // CPU から見た相手1人(生存者または幽霊)
    public class CpuOpponent
    {
        public MatchPlayer Player { get; }
        public bool IsGhost { get; internal set; }
        public Vector3 Position { get; internal set; }
        // 位置の変化から測った水平の速度
        public Vector3 Velocity { get; internal set; }
        public int StackCount { get; internal set; }
        // 段数の差(自分 − 相手)。プラスなら自分が有利
        public int StackDiff { get; internal set; }
        public bool IsInvincible { get; internal set; }
        // 生存者の体(幽霊なら null)
        public DarumaCharacter Character { get; internal set; }
        // 薙ぎ払いを振っている最中か(生存者のみ。見た目で分かる情報)
        public bool IsSweeping { get; internal set; }
        // 空中にいるか(生存者のみ。跳んでいる相手には薙ぎ払いが当たらない)
        public bool IsAirborne { get; internal set; }
        // この相手の薙ぎ払いが自分に届く、中心同士の距離(生存者のみ)
        public float SweepHitDistance { get; internal set; }
        // 自分からの水平の距離
        public float FlatDistance { get; internal set; }
        // 自分からの道の距離(歩いて行けなければ無限大。幽霊は使わない)
        public float PathDistance { get; internal set; } = float.PositiveInfinity;
        // 脅威として見る距離:幽霊は奈落の上も浮いて来るのでまっすぐの距離、生存者は歩いて来るので道の距離
        public float ThreatDistance => IsGhost ? FlatDistance : PathDistance;

        internal Vector3 LastPosition;
        internal float LastSampleTime = -1.0f;

        public CpuOpponent(MatchPlayer player)
        {
            Player = player;
        }
    }

    // 知る:自分以外の相手の様子を毎フレームまとめる(全状態が同じ情報を見る)
    // 道の距離は重いので 0.2 秒ごと(CpuParams.ThinkInterval)に測り直す
    public class CpuPerception
    {
        // 相手までの道を求めるとき、相手の位置のこの距離(m)以内まで行ければよい(ジャンプ中の相手も測れるように)
        const float kOpponentGoalTolerance = 2.5f;
        // 測った速度をなめらかにする速さ
        const float kVelocitySmoothing = 15.0f;

        readonly Dictionary<MatchPlayer, CpuOpponent> m_known = new Dictionary<MatchPlayer, CpuOpponent>();
        readonly List<CpuOpponent> m_opponents = new List<CpuOpponent>();
        NavMeshPath m_path;
        float m_pathTimer;

        // 体がある相手(生存者・幽霊)。復活待ち・脱落した人は含まない
        public IReadOnlyList<CpuOpponent> Opponents => m_opponents;

        public void Update(CpuContext c)
        {
            if (m_path == null) m_path = new NavMeshPath();
            m_opponents.Clear();
            var match = DarumaMatch.Current;
            if (match == null) return;

            m_pathTimer -= c.DeltaTime;
            bool measurePaths = m_pathTimer <= 0.0f;
            if (measurePaths) m_pathTimer = c.Params.ThinkInterval;

            Vector3 selfPos = c.Position;
            foreach (var p in match.Players)
            {
                if (p == null || p == c.Player || p.Body == null || p.Body == c.Self.transform) continue;
                if (!m_known.TryGetValue(p, out var o))
                {
                    o = new CpuOpponent(p);
                    m_known.Add(p, o);
                }

                bool wasGhost = o.IsGhost;
                o.IsGhost = p.Character == null;
                Vector3 pos = p.Body.position;
                // 生存者⇔幽霊が入れ替わった・復活で出し直されたときは、速度を測り直す
                if (wasGhost != o.IsGhost || (o.LastSampleTime >= 0.0f && CpuContext.FlatDistance(pos, o.LastPosition) > 3.0f)) o.LastSampleTime = -1.0f;
                o.Position = pos;
                MeasureVelocity(o);
                o.StackCount = p.StackCount;
                o.StackDiff = c.StackCount - o.StackCount;
                o.IsInvincible = p.IsInvincible;
                o.Character = p.Character;
                // ハンマーが実際に動き始めてから(物理が1ステップ進んでから)見えるようにする
                // (ボタンを押したフレームに見えると、後から操作命令を読むキャラクターだけが1フレーム早く反応できて不公平)
                o.IsSweeping = o.Character != null && o.Character.Hammer.IsSweeping && o.Character.Hammer.ActionTime > 0.0f;
                o.IsAirborne = o.Character != null && !o.Character.IsGrounded;
                o.SweepHitDistance = o.Character != null ? o.Character.Hammer.CharacterHitDistance : 0.0f;
                o.FlatDistance = CpuContext.FlatDistance(selfPos, pos);
                if (o.IsGhost)
                {
                    o.PathDistance = float.PositiveInfinity;
                }
                else if (measurePaths)
                {
                    o.PathDistance = CpuNavMesh.TryFindPath(selfPos, pos, kOpponentGoalTolerance, m_path, out float length)
                        ? length
                        : float.PositiveInfinity;
                }
                m_opponents.Add(o);
            }
        }

        // 相手の情報(体がなければ null)
        public CpuOpponent Find(MatchPlayer player)
        {
            foreach (var o in m_opponents)
            {
                if (o.Player == player) return o;
            }
            return null;
        }

        // 物理の1ステップごとの位置の変化から速度を測る(物理が進んでいないフレームは測らない)
        private static void MeasureVelocity(CpuOpponent o)
        {
            float now = Time.fixedTime;
            if (now == o.LastSampleTime) return;
            if (o.LastSampleTime >= 0.0f)
            {
                float dt = now - o.LastSampleTime;
                Vector3 v = (o.Position - o.LastPosition) / dt;
                v.y = 0.0f;
                o.Velocity = Vector3.Lerp(o.Velocity, v, 1.0f - Mathf.Exp(-kVelocitySmoothing * dt));
            }
            else
            {
                o.Velocity = Vector3.zero;
            }
            o.LastPosition = o.Position;
            o.LastSampleTime = now;
        }
    }
}
