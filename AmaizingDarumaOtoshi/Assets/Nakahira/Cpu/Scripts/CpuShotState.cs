using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 状態「撃つ」:股抜きショットで自分の最下段を撃ち出す(撃つと自分が 1 段減る)
    //  撃つ理由(動機):
    //   ・狙い撃ち  :身の回り(ShotNearbyRadius)に拾える積み木がなく、残しておきたい段数(ShotReserve)より多いとき、
    //                 殴る間合いより遠く ShotRange 以内の、倒せそうな相手(攻めの点数が一番高い相手)を撃つ
    //   ・熱中      :撃ち始めるたびに「熱中する確率 × 焦り」で熱中する。熱中中は残しておきたい段数を無視し(首だけになることも)、
    //                 撃つ間隔も短くなる
    //   ・追い撃ち  :殴り合いの相手が間合いの外へ逃げたとき、少しの間は近い距離(PursuitMinDistance)からでも撃つ。
    //                 残しておきたい段数は無視する(ただし撃って首だけにはならない)
    //   ・乱反射遊び:拾いに行くには遠すぎる(道がない・遠回りしかない)場所に固まっている積み木へ、たまに撃ち込む
    //  撃ち方:相手の動きを先読みして(偏差撃ち)、腕前のぶれ(ShotAimError)を足した向きへ撃つ
    //  ついでに弾く:撃ち合いの最中、ハンマーの届く範囲に落ちている積み木を弾けば相手に当たる向きなら、撃つより先に薙ぎ払いで弾く
    //               (自分の積み木が減らない。回り込みはせず、たまたま向きが合ったときだけ)
    //  撃ったら集中度を少し足す。撃ったあとは普通の状態選びで、積む・殴るなどへ移る
    public class CpuShotState : CpuState
    {
        enum Motive { None, Hunt, Pursuit, Scatter }

        // 撃つ向きへスティックを倒す強さ(ショットは押した瞬間にスティックの向きを向き、弾が出るまで追従する。歩かないよう小さく)
        const float kAimInput = 0.05f;
        // 乱反射遊び:狙う積み木の固まりを覚えておく時間(秒)
        const float kScatterMemoryTime = 2.0f;
        // 撃つ線上の壁を調べる高さ(足元から。弾の中心の高さ)
        const float kShotHeight = TumikiBlock.kHeight * 0.5f;
        // ついでに弾く:ハンマーの届く距離より、この距離(m)だけ内側の積み木を弾く(ぎりぎりだと届かないことがある)
        const float kFlickReachMargin = 0.2f;
        // ついでに弾く:体に近すぎる積み木は弾かない(m)
        const float kFlickMinDistance = 0.6f;
        // ついでに弾く:飛ばせる向きの最大角度より、この角度(度)だけ内側の積み木を弾く
        const float kFlickAngleMargin = 5.0f;
        // ついでに弾く:弾いたあと、次に弾くまでの間(秒。振り終わってから)
        const float kFlickInterval = 0.2f;

        // 撃ったあと横へ動くとき、相手との距離をこの範囲(m)に保つ(殴る間合いの少し外 〜 射程の少し内)
        const float kStrafeNearMargin = 1.0f;
        const float kStrafeFarMargin = 1.0f;
        // 横へ動くとき、距離のずれを前後に直す強さ
        const float kStrafeRadialGain = 1.0f;
        // 横へ動く先に床があるか確かめる距離(m)
        const float kStrafeLookAhead = 0.8f;
        // 踏み込みながら撃つのは、相手が殴る間合いよりこの距離(m)以上遠いときだけ
        const float kStepInMinGap = 1.5f;

        Motive m_motive;
        MatchPlayer m_target;
        // 撃ったあと横へ動く向き(+1 = 相手を左に見て右へ、-1 = 反対、0 = 止まって撃つ)
        int m_strafeSide;
        // 今の1発を、相手の方へ踏み込みながら狙うか
        bool m_stepIn;
        Vector3 m_scatterPoint;
        float m_scatterUntil = -1.0f;
        float m_nextScatterRoll;
        float m_nextShotTime;
        bool m_overheated;
        // 撃つボタンを押して、弾が出るのを待っている
        bool m_aiming;
        float m_aimError;
        NavMeshPath m_path;   // MonoBehaviour の初期化中には作れないので、使うときに作る
        // ついでに弾く:次に弾ける時刻と、弾くか決めた積み木(積み木ごとに一度だけ決める)
        float m_nextFlickTime;
        readonly System.Collections.Generic.HashSet<TumikiBlock> m_flickRolled = new System.Collections.Generic.HashSet<TumikiBlock>();

        public override string Name => "撃つ";

        public override float Threshold(CpuContext c) => c.Params.ShotThreshold;

        public override float Evaluate(CpuContext c)
        {
            if (m_aiming) return c.Params.ShotScore;   // 撃っている最中は撃ち終わるまで続ける
            return ChooseMotive(c, out _, out _) != Motive.None ? c.Params.ShotScore : 0.0f;
        }

        public override void Enter(CpuContext c)
        {
            m_motive = ChooseMotive(c, out m_target, out m_scatterPoint);
            // 熱中するか(疲れて焦っているほど熱中しやすい)
            m_overheated = Random.value < c.Params.OverheatChance * c.Panic;
            m_aiming = false;
            RollStrafe(c);
            m_stepIn = false;
        }

        // 次に撃つまでの間に横へ動くか・どちらへ動くか決める(外れたらその場で止まって撃つ)
        private void RollStrafe(CpuContext c)
        {
            m_strafeSide = Random.value < c.Params.ShotStrafeChance ? (Random.value < 0.5f ? 1 : -1) : 0;
        }

        public override void Exit(CpuContext c)
        {
            m_aiming = false;
            m_target = null;
            m_motive = Motive.None;
            m_overheated = false;
            m_flickRolled.Clear();
        }

        public override bool Tick(CpuContext c, ref DarumaCommand cmd)
        {
            DarumaHammer hammer = c.Self.Hammer;

            // 弾が出るまで狙い続ける(ショットは弾が出るまでスティックの向きに追従する)
            if (m_aiming)
            {
                if (hammer.IsAimingShot)
                {
                    // 狙う間はスティックの向きが狙う向きになる。踏み込む番なら、その向きへ歩きながら狙う
                    if (TryGetAim(c, out Vector3 dir))
                    {
                        bool step = m_stepIn && CpuSensor.HasGround(c.Position + dir * kStrafeLookAhead, c.Params.GroundProbeDepth);
                        cmd.Move = new Vector2(dir.x, dir.z) * (step ? 1.0f : kAimInput);
                    }
                    return true;
                }
                // 弾が出た(または撃てなかった)
                m_aiming = false;
                if (!hammer.IsShooting) return true;
                c.AddLoad(c.Params.AttentionOnShot);
                m_nextShotTime = Time.time + NextShotInterval(c);
                // 撃ったあとは横へ動く(たまにその場で止まって撃つ)。弾が出たあとは向きが固定されるので、横へ走っても狙いはずれない
                RollStrafe(c);
                if (m_strafeSide != 0) Strafe(c, ref cmd);
                return true;
            }

            // 撃つ理由がなくなったら終わり(普通の状態選びに戻る)
            m_motive = ChooseMotive(c, out m_target, out m_scatterPoint);
            if (m_motive == Motive.None) return false;

            // 狙う向き
            if (!TryGetAim(c, out Vector3 aim)) return false;

            // ついでに弾く:ハンマーの届く範囲の積み木を弾けば相手に当たるなら、撃つより先に弾く(自分の積み木が減らない)
            if (m_motive != Motive.Scatter && TryFlick(c))
            {
                cmd.Sweep = true;
                cmd.Move = new Vector2(aim.x, aim.z) * kAimInput;
                return true;
            }

            if (Time.time < m_nextShotTime || hammer.IsBusy || !c.Self.CanStartShot)
            {
                // 待つ間は横へ動く(撃つ瞬間にスティックの向きへ向き直るので、走っていても狙える)。止まる番なら狙う向きを向いておく
                if (m_strafeSide == 0 || !Strafe(c, ref cmd)) cmd.Move = new Vector2(aim.x, aim.z) * kAimInput;
                return true;
            }

            // 撃つ(押した瞬間にスティックの向きを向く)
            // 狙いのぶれ:集中度の割合が高いほど大きい(落ち着いていれば精密、焦ると連打で雑)
            float error = Mathf.Lerp(c.Params.ShotAimError, c.Params.ShotAimErrorMax, c.LoadRatio);
            m_aimError = Random.Range(-error, error);
            TryGetAim(c, out aim);
            cmd.Move = new Vector2(aim.x, aim.z) * kAimInput;
            cmd.Shot = true;
            m_aiming = true;
            // 踏み込みながら狙うか(相手が殴る間合いより十分遠いときだけ)
            m_stepIn = m_motive != Motive.Scatter && TargetDistance(c) > c.Params.MeleeEnterDistance + kStepInMinGap
                && Random.value < c.Params.ShotStepInChance;
            return true;
        }

        // 次に撃つまでの間(秒)。熱中していれば短い。そうでなければ、集中度の割合が高いほど連打に近づく
        private float NextShotInterval(CpuContext c)
        {
            CpuParams p = c.Params;
            float calm = Random.Range(p.ShotIntervalMin, p.ShotIntervalMax);
            float mash = Random.Range(0.0f, p.ShotMashInterval);
            float interval = Mathf.Lerp(calm, mash, c.LoadRatio);
            return m_overheated ? Mathf.Min(interval, p.OverheatShotInterval) : interval;
        }

        // 狙う相手(乱反射遊びなら固まり)までの水平の距離
        private float TargetDistance(CpuContext c)
        {
            if (m_motive == Motive.Scatter) return CpuContext.FlatDistance(c.Position, m_scatterPoint);
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            return o != null ? o.FlatDistance : 0.0f;
        }

        // 相手(乱反射遊びなら固まり)を横に見ながら、距離を保って横へ走る。横に床がなければ反対へ。どちらも無理なら false
        private bool Strafe(CpuContext c, ref DarumaCommand cmd)
        {
            CpuParams p = c.Params;
            Vector3 targetPos;
            if (m_motive == Motive.Scatter) targetPos = m_scatterPoint;
            else
            {
                CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
                if (o == null) return false;
                targetPos = o.Position;
            }
            Vector3 to = targetPos - c.Position;
            to.y = 0.0f;
            float dist = to.magnitude;
            if (dist < 0.01f) return false;
            to /= dist;

            // 近すぎれば下がり、遠すぎれば寄る(殴る間合いの少し外 〜 射程の少し内)
            float near = p.MeleeEnterDistance + kStrafeNearMargin;
            float far = Mathf.Max(near, p.ShotRange - kStrafeFarMargin);
            float radial = dist < near ? (dist - near) : (dist > far ? dist - far : 0.0f);
            Vector3 radialDir = to * Mathf.Clamp(radial * kStrafeRadialGain, -1.0f, 1.0f);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Vector3 tangent = Vector3.Cross(Vector3.up, to) * m_strafeSide;
                Vector3 dir = (tangent + radialDir).normalized;
                Vector3 ahead = c.Position + dir * kStrafeLookAhead;
                if (CpuSensor.HasGround(ahead, p.GroundProbeDepth) && !CpuNavMesh.IsNearHazard(ahead, DarumaCharacter.kBodyRadius))
                {
                    cmd.Move = new Vector2(dir.x, dir.z);
                    return true;
                }
                m_strafeSide = -m_strafeSide;   // こちらは床がない(奈落ぎわ・バンパー):反対へ
            }
            return false;
        }

        //========================================
        // 撃つ理由を選ぶ
        //========================================

        private Motive ChooseMotive(CpuContext c, out MatchPlayer target, out Vector3 scatterPoint)
        {
            target = null;
            scatterPoint = Vector3.zero;
            CpuParams p = c.Params;
            int stack = c.StackCount;
            if (stack <= 0) return Motive.None;

            // 追い撃ち:殴り合いから逃げた相手(撃って首だけにはならない)
            if (stack >= 2 && Time.time - c.FledTime <= p.PursuitWindow && c.FledTarget != null)
            {
                CpuOpponent o = c.Perception.Find(c.FledTarget);
                if (IsShootable(c, o) && o.FlatDistance >= p.PursuitMinDistance && o.FlatDistance <= p.ShotRange)
                {
                    target = o.Player;
                    return Motive.Pursuit;
                }
            }

            // 残しておきたい段数(熱中していなければ守る)
            bool overheated = m_overheated && m_motive != Motive.None;
            int reserve = overheated ? 0 : p.ShotReserve;
            if (stack <= reserve) return Motive.None;

            // 狙い撃ち:身の回りに拾える積み木がないとき、遠くの倒せそうな相手を撃つ
            // (身の回りを確かめるのは撃ち始めるときだけ。撃ち合いの最中に近くに落ちてきた積み木は、ついでに弾く材料にする)
            // 満タン近く(遊ぶ)なら、近くに積み木があっても撃つ
            bool engaged = m_motive == Motive.Hunt;
            if (engaged || c.IsPlayful || !HasNearbyCollectable(c))
            {
                CpuOpponent best = null;
                float bestScore = float.MinValue;
                foreach (var o in c.Perception.Opponents)
                {
                    if (!IsShootable(c, o)) continue;
                    if (o.FlatDistance <= p.MeleeEnterDistance || o.FlatDistance > p.ShotRange) continue;
                    float score = CpuAttackTarget.Score(c, o, o.FlatDistance);
                    if (o.Player == m_target) score += p.StateStickiness;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = o;
                    }
                }
                if (best != null)
                {
                    target = best.Player;
                    return Motive.Hunt;
                }
            }

            // 乱反射遊び:たまに、拾いに行けない積み木の固まりへ撃ち込む
            if (TryGetScatterPoint(c, out scatterPoint)) return Motive.Scatter;
            return Motive.None;
        }

        // 撃てる相手か(生存者・無敵でない・撃つ線上に壁がない)
        private static bool IsShootable(CpuContext c, CpuOpponent o)
        {
            if (o == null || o.IsGhost || o.IsInvincible || o.Character == null) return false;
            return !HasWall(c.Position, o.Position);
        }

        // 身の回り(ShotNearbyRadius)に拾いに行ける積み木があるか(取り合いで譲るべき積み木は数えない)
        private static bool HasNearbyCollectable(CpuContext c)
        {
            float radius = c.Params.ShotNearbyRadius;
            foreach (var block in TumikiBlock.All)
            {
                if (!CpuBlockSearch.IsCollectable(block) || c.IsGivenUp(block)) continue;
                Vector3 bp = block.transform.position;
                if (CpuContext.FlatDistance(c.Position, bp) > radius) continue;
                if (!CpuBlockSearch.CanReachHeight(c.Self, bp)) continue;
                if (CpuBlockSearch.ShouldYield(c, bp)) continue;
                return true;
            }
            return false;
        }

        // 拾いに行くには遠すぎる積み木の固まり(半径 ScatterClusterRadius に ScatterClusterCount 個以上。
        // 歩く道がない・道の長さが ScatterMinPathLength 以上)を、たまに見つける
        private bool TryGetScatterPoint(CpuContext c, out Vector3 point)
        {
            point = m_scatterPoint;
            if (Time.time < m_scatterUntil) return true;

            // ThinkInterval ごとに1回だけくじを引く(呼ばれる間隔によらないように時刻で数える)
            CpuParams p = c.Params;
            if (Time.time < m_nextScatterRoll) return false;
            m_nextScatterRoll = Time.time + p.ThinkInterval;
            // 満タン近く(遊ぶ)なら、よく撃ち込みたくなり、歩いて拾える近くの固まりにも撃つ
            bool playful = c.IsPlayful;
            float chance = playful ? p.PlayfulScatterChancePerSecond : p.ScatterChancePerSecond;
            if (Random.value >= chance * p.ThinkInterval) return false;

            Vector3 pos = c.Position;
            float minDist = playful ? p.MeleeEnterDistance : p.ShotNearbyRadius;
            foreach (var block in TumikiBlock.All)
            {
                if (!CpuBlockSearch.IsCollectable(block)) continue;
                Vector3 bp = block.transform.position;
                float dist = CpuContext.FlatDistance(pos, bp);
                if (dist <= minDist || dist > p.ScatterRange) continue;
                int count = 0;
                foreach (var other in TumikiBlock.All)
                {
                    if (CpuBlockSearch.IsCollectable(other) && CpuContext.FlatDistance(other.transform.position, bp) <= p.ScatterClusterRadius) count++;
                }
                if (count < p.ScatterClusterCount) continue;
                // 近い道で拾いに行ける固まりは撃たない(拾いに行く)。道がない・遠回りの道しかない固まりだけ
                if (m_path == null) m_path = new NavMeshPath();
                if (!playful && CpuNavMesh.TryFindPath(pos, bp, CpuPickupMotor.GoalTolerance, m_path, out float length)
                    && length < p.ScatterMinPathLength) continue;
                if (HasWall(pos, bp)) continue;
                point = bp;
                m_scatterUntil = Time.time + kScatterMemoryTime;
                return true;
            }
            return false;
        }

        //========================================
        // ついでに弾く
        //========================================

        // ハンマーの届く範囲に落ちている積み木を弾けば、相手に当たるか。当たるなら弾く(true)
        // ハンマーは積み木を「自分から見た積み木の方向」へ飛ばすので、その線上を相手が通るかを、相手の動きを先読みして調べる
        // (回り込み・壁での反射は考えない。たまたま向きが合ったときだけ)
        private bool TryFlick(CpuContext c)
        {
            CpuParams p = c.Params;
            DarumaCharacter self = c.Self;
            DarumaHammer hammer = self.Hammer;
            if (Time.time < m_nextFlickTime || hammer.IsBusy || !self.IsGrounded) return false;
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            if (o == null) return false;

            Vector3 pos = c.Position;
            Vector3 forward = self.transform.forward;
            forward.y = 0.0f;
            float reach = hammer.BlockHitDistance - kFlickReachMargin;
            float speed = Mathf.Max(1.0f, hammer.SweepLaunchSpeed);
            foreach (var block in TumikiBlock.All)
            {
                // 落ちている積み木だけ(攻撃判定のある弾は、とっさの反応の打ち返しに任せる)
                if (block == null || block.CurrentState != TumikiBlock.State.Loose || !block.CanBeAffected) continue;
                if (m_flickRolled.Contains(block)) continue;
                Vector3 bp = block.transform.position;
                if (!hammer.IsAtSweepHeight(bp.y - pos.y)) continue;
                Vector3 rel = bp - pos;
                rel.y = 0.0f;
                float dist = rel.magnitude;
                if (dist < kFlickMinDistance || dist > reach) continue;
                // ハンマーが飛ばせる向き(正面の左右)の中にある積み木だけ
                float bearing = Vector3.SignedAngle(forward, rel, Vector3.up);
                if (Mathf.Abs(bearing) > hammer.MaxLaunchAngle - kFlickAngleMargin) continue;

                // 弾いた積み木が飛ぶ線(積み木から、自分から見た積み木の方向へ)の上を、相手が通るか
                Vector3 dir = rel / dist;
                float cross = Mathf.Clamp01((hammer.SweepStartYaw - bearing) / (hammer.SweepStartYaw - hammer.SweepEndYaw)) * hammer.SweepDuration;
                Vector3 target = o.Position;
                for (int i = 0; i < 2; i++)
                {
                    float flight = CpuContext.FlatDistance(bp, target) / speed;
                    target = o.Position + o.Velocity * (cross + flight);
                }
                Vector3 toTarget = target - bp;
                toTarget.y = 0.0f;
                float along = Vector3.Dot(toTarget, dir);
                if (along <= 0.0f || along > p.ShotRange) continue;
                float miss = (toTarget - dir * along).magnitude;
                if (miss > p.FlickAimWidth) continue;

                // 弾くか決める(積み木ごとに一度だけ。強いほど逃さない)
                m_flickRolled.Add(block);
                if (Random.value >= p.FlickChance) continue;
                m_nextFlickTime = Time.time + hammer.SweepDuration + kFlickInterval;
                c.AddLoad(p.AttentionOnShot);
                return true;
            }
            return false;
        }

        //========================================
        // 狙う
        //========================================

        // 撃つ向き(水平の単位ベクトル)。相手の動きを先読みし(偏差撃ち)、腕前のぶれを足す
        private bool TryGetAim(CpuContext c, out Vector3 dir)
        {
            dir = Vector3.zero;
            Vector3 pos = c.Position;
            Vector3 aim;
            if (m_motive == Motive.Scatter)
            {
                aim = m_scatterPoint;
            }
            else
            {
                CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
                if (o == null) return false;
                // 弾が届く時刻の相手の位置(届く時間は予想した位置までの距離で求め直す)
                DarumaHammer hammer = c.Self.Hammer;
                float delay = hammer.IsAimingShot ? Mathf.Max(0.0f, hammer.ShotFireDelay - hammer.ActionTime) : hammer.ShotFireDelay;
                aim = o.Position;
                for (int i = 0; i < 2; i++)
                {
                    float flight = CpuContext.FlatDistance(pos, aim) / Mathf.Max(1.0f, hammer.ShotSpeed);
                    aim = o.Position + o.Velocity * (delay + flight);
                }
            }
            Vector3 to = aim - pos;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) return false;
            dir = Quaternion.AngleAxis(m_aimError, Vector3.up) * to.normalized;
            return true;
        }

        // from から to までの間に壁(積み木・キャラクター以外の当たり判定)があるか
        private static bool HasWall(Vector3 from, Vector3 to)
        {
            Vector3 a = from + Vector3.up * kShotHeight;
            Vector3 b = new Vector3(to.x, from.y, to.z) + Vector3.up * kShotHeight;
            Vector3 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return false;
            foreach (var hit in Physics.RaycastAll(a, d / length, length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                return true;
            }
            return false;
        }

        public override void DrawGizmos(CpuContext c)
        {
            Gizmos.color = Color.magenta;
            if (m_motive == Motive.Scatter)
            {
                Gizmos.DrawLine(c.Position + Vector3.up * kShotHeight, m_scatterPoint);
                Gizmos.DrawWireSphere(m_scatterPoint, c.Params.ScatterClusterRadius);
                return;
            }
            CpuOpponent o = m_target != null ? c.Perception.Find(m_target) : null;
            if (o != null) Gizmos.DrawLine(c.Position + Vector3.up * kShotHeight, o.Position + Vector3.up * kShotHeight);
        }
    }
}
