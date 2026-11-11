using System;
using System.Collections.Generic;
using UnityEngine;
using Yurube;
using Random = UnityEngine.Random;

namespace Nakahira
{
    // CPU が必殺技を撃つ(1人の CPU につき1つ)
    // ゲージが満タンで、そのキャラの発動条件(ShouldActivate)を満たしたら撃つ
    // 今はほとんどのキャラが「満タンになったらすぐ撃つ」。キャラごとの撃ちどころは ShouldActivate に足す(設計メモ 6.2.1)
    //
    // あおだる(六腕)は次の 2 つのときだけ撃つ
    //  ・暗転返し:みだるのファンネルのカットイン中(時間が止まっている間)に撃つ。
    //    暗転する前に、みだるが明らかに近かった(お互いに一番近い敵)か、みだると戦っていた(殴る・逃げる・撃つ・攻めに行く)ときだけ。
    //    人も、それ以外のときはファンネルが自分に来るか分からないので
    //  ・とっさの六腕:自分に当たる弾が 2 発以上あるとき、または追い込まれている(段数が少なく、集中度の割合が高い)ときに弾が来たとき
    public class CpuSpecial
    {
        // ストーリー用(Yurube): 章の設定でCPUの必殺技を止める（チュートリアルなど）
        public static bool Disabled;

        DarumaCharacter m_self;
        ISpecialAbility m_ability;
        SpecialGauge m_gauge;

        // 暗転返し:今のみだるのカットインを見たか・返すと決めたか・暗転し始めた時刻(止まらない時間)
        bool m_blackoutSeen;
        bool m_blackoutCounter;
        float m_blackoutStart;

        // とっさの六腕:撃つか決めた弾(弾ごとに一度だけ決める)・撃つ時刻(反応の遅れのあと)
        readonly HashSet<TumikiBlock> m_armsDecided = new HashSet<TumikiBlock>();
        readonly List<TumikiBlock> m_armsThreats = new List<TumikiBlock>();
        readonly List<TumikiBlock> m_armsForget = new List<TumikiBlock>();
        bool m_armsPending;
        float m_armsFireAt;

        // とっさの六腕:撃つと決めてから、撃てないまま(ハンマーを振っているなど)あきらめるまでの時間(秒)
        const float kArmsGiveUp = 0.3f;

        // 撃ちどころ(あおだる以外):満タンになってからまだ撃っていないか・すぐ撃ってしまうか
        bool m_wasFull;
        bool m_rash;
        // 撃ちどころが続いているか・気づいたか・次に気づくか試す時刻
        bool m_inChance;
        bool m_noticed;
        float m_nextNotice;

        // 撃ちどころを見逃したとき、引き直すまでの間隔(秒)
        const float kRenoticeInterval = 1.0f;

        // あかだる(トロン):狙う相手・向き直っている最中か・狙う向き・狙いのぶれ(度。向き直り始めに一度だけ決める)
        MatchPlayer m_tronTarget;
        bool m_tronAiming;
        Vector3 m_tronAimDir;
        float m_tronAimError;
        // 向き直るときにスティックを倒す強さ(歩かないよう小さく)
        const float kAimInput = 0.05f;
        // 狙う向きとの差がこれ(度)以内になったら撃つ
        const float kTronFaceAngle = 4.0f;

        // 撃つと決めた理由を Console に出す(テスト用)
        public static bool LogDecisions;

        // 撃った回数(デバッグ・テスト用)
        public int ActivatedCount { get; private set; }
        // 暗転返しをした回数(デバッグ・テスト用)
        public int BlackoutCounterCount { get; private set; }
        // ゲージが満タンか(デバッグ表示用)
        public bool IsReady => m_gauge != null && m_gauge.IsFull;

        public void Reset()
        {
            m_self = null;
            m_ability = null;
            m_gauge = null;
            m_blackoutSeen = false;
            m_blackoutCounter = false;
            m_armsDecided.Clear();
            m_armsPending = false;
            m_wasFull = false;
            ForgetChance();
        }

        // 撃てたら true。engaged:その相手と戦っている最中か(殴る・逃げる・撃つ・攻めに行く)。current:今の状態
        // 時間が止まっている間(カットイン中)も毎フレーム呼ばれる
        public bool Apply(CpuContext c, Func<MatchPlayer, bool> engaged, CpuState current)
        {
            if (Disabled)
            {
                ForgetChance();
                return false;
            }
            Bind(c.Self);
            if (m_ability == null || m_gauge == null) return false;

            // 暗転返しは時間が止まっている間に撃つので、下の「時間が止まっている間は撃たない」より先に見る
            if (m_ability is AodaruSpecial && UpdateBlackoutCounter(c, engaged)) return Activated();

            if (!m_gauge.IsFull)
            {
                m_wasFull = false;
                ForgetChance();
                return false;
            }
            // 時間が止まっている・遅くなっている間(ほかの必殺技のカットイン・ヒットストップ・やられ演出)は撃たない
            if (Time.timeScale < DarumaMatch.PlaySpeed) return false;

            if (m_ability is AodaruSpecial) return UpdateReflexArms(c) && Activated();

            // 満タンになった瞬間に、撃ちどころを待たずにすぐ撃ってしまうかを決める(よわいほど多い)
            if (!m_wasFull)
            {
                m_wasFull = true;
                m_rash = Random.value < c.Params.SpecialRashChance;
                ForgetChance();
                if (LogDecisions && m_rash) Debug.Log($"[CPU 必殺技] {c.Self.name} 満タン → 撃ちどころを待たずに撃つ");
            }
            if (!m_rash && !Notice(c, ShouldActivate(c, current))) return false;
            // あかだる:狙う向きへ向き直ってから撃つ(トロンは押した瞬間の向きへ飛ぶ。すぐ撃ってしまうときは今の向きのまま)
            if (!m_rash && m_ability is AkadaruSpecial akadaru && !FacingTronAim(c, akadaru)) return false;
            // 撃てない間(ハンマーを振っているなど)は、撃ちどころが続く間 毎フレーム試す
            if (!m_ability.TryActivate()) return false;
            m_wasFull = false;
            ForgetChance();
            return Activated();
        }

        // 撃ちどころに気づいたか。撃ちどころが来てから SpecialNoticeDelay 秒後に、余裕(1 − 集中度の割合)の確率で気づく。
        // 見逃したら、撃ちどころが続いている間 kRenoticeInterval 秒ごとに引き直す
        private bool Notice(CpuContext c, bool chance)
        {
            if (!chance)
            {
                ForgetChance();
                return false;
            }
            if (!m_inChance)
            {
                m_inChance = true;
                m_noticed = false;
                m_nextNotice = Time.time + c.Params.SpecialNoticeDelay;
            }
            if (m_noticed) return true;
            if (Time.time < m_nextNotice) return false;
            m_nextNotice = Time.time + kRenoticeInterval;
            m_noticed = Random.value < c.Alertness;
            if (LogDecisions) Debug.Log($"[CPU 必殺技] {c.Self.name} 撃ちどころ → 気づいた={m_noticed}(余裕 {c.Alertness:0.00})");
            return m_noticed;
        }

        private void ForgetChance()
        {
            m_inChance = false;
            m_noticed = false;
            m_tronAiming = false;
        }

        // 撃つ向きへ向き直っている最中なら、状態が出した操作を置き換える(状態の Tick のあとに呼ぶ)
        // 狙う向きへスティックを少しだけ倒す(歩かずに向きだけ変える)。ハンマー・ジャンプは止める
        public void OverrideAim(ref DarumaCommand cmd)
        {
            if (!m_tronAiming) return;
            cmd.Move = new Vector2(m_tronAimDir.x, m_tronAimDir.z) * kAimInput;
            cmd.Jump = false;
            cmd.Sweep = false;
            cmd.Shot = false;
        }

        private bool Activated()
        {
            ActivatedCount++;
            return true;
        }

        // 体が変わったら必殺技とゲージを取り直す(パッシブがあとから付けることもあるので、見つかるまで探す)
        private void Bind(DarumaCharacter self)
        {
            if (self != m_self)
            {
                m_self = self;
                m_ability = null;
                m_gauge = null;
                m_armsDecided.Clear();
                m_armsPending = false;
                m_wasFull = false;
                ForgetChance();
            }
            if (m_self == null) return;
            if (m_ability == null) m_ability = m_self.GetComponent<ISpecialAbility>();
            if (m_gauge == null) m_gauge = m_self.GetComponent<SpecialGauge>();
        }

        // キャラごとの撃ちどころ(ゲージが満タンのときだけ呼ばれる。あおだるは別)
        // まだ撃ちどころを決めていないキャラは true(満タンになったら、気づく遅れのあとに撃つ)
        private bool ShouldActivate(CpuContext c, CpuState current)
        {
            switch (m_ability)
            {
                case IwadaruSpecial _:  return true;   // 隕石
                case HebidaruSpecial _: return true;   // 毒沼
                case AkadaruSpecial _:  return TronChance(c);   // トロン(ビーム)
                case DaruKingSpecial _: return true;   // エクスカリバー
                case MidaruSpecial _:   return true;   // ファンネル
                case KidaruSpecial _:   return KidaruChance(c, current);   // 黄金化
                default:                return true;
            }
        }

        // きだる(黄金化):攻めるとき。殴っている最中か、攻めに行っていて相手まで道で GoldenFireDistance 以内
        // (遠くで撃って効き目を無駄にしない)
        private static bool KidaruChance(CpuContext c, CpuState current)
        {
            if (current is CpuMeleeState melee) return melee.Target != null;
            if (current is CpuApproachState approach && approach.Target != null)
            {
                CpuOpponent o = c.Perception.Find(approach.Target);
                return o != null && o.PathDistance <= c.Params.GoldenFireDistance;
            }
            return false;
        }

        // あかだる(トロン):どの状態の最中でも、TronRange 以内に間に壁のない敵がいるとき(積み木が 1 個以上あるとき)
        // 狙う相手は、攻めの点数が一番高い敵(今狙っている相手を少し優先)
        // 自分の通り道をふさぐ向きでも気にしない(自分のビームに当たって自滅しても、それはそれで面白い。中平が決めた)
        private bool TronChance(CpuContext c)
        {
            if (c.StackCount <= 0) return false;
            CpuOpponent best = null;
            float bestScore = float.MinValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost || o.IsInvincible || o.Character == null) continue;
                if (o.FlatDistance > c.Params.TronRange) continue;
                if (CpuShotState.HasWall(c.Position, o.Position)) continue;
                float score = CpuAttackTarget.Score(c, o, o.FlatDistance);
                if (o.Player == m_tronTarget) score += c.Params.StateStickiness;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            m_tronTarget = best != null ? best.Player : null;
            return best != null;
        }

        // 狙う向き(相手の動きを先読みし、腕前のぶれを足す)へ向き直ったか
        private bool FacingTronAim(CpuContext c, AkadaruSpecial akadaru)
        {
            CpuOpponent o = m_tronTarget != null ? c.Perception.Find(m_tronTarget) : null;
            if (o == null) return false;
            if (!m_tronAiming)
            {
                // 狙いのぶれは股抜きショットと同じ:集中度の割合が高いほど大きい
                float error = Mathf.Lerp(c.Params.ShotAimError, c.Params.ShotAimErrorMax, c.LoadRatio);
                m_tronAimError = Random.Range(-error, error);
                m_tronAiming = true;
            }

            // 弾が届く時刻の相手の位置(届く時間は予想した位置までの距離で求め直す)
            Vector3 pos = c.Position;
            Vector3 aim = o.Position;
            float speed = Mathf.Max(1.0f, akadaru.Speed);
            for (int i = 0; i < 2; i++)
            {
                float flight = CpuContext.FlatDistance(pos, aim) / speed;
                aim = o.Position + o.Velocity * flight;
            }
            Vector3 to = aim - pos;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) return false;
            m_tronAimDir = Quaternion.AngleAxis(m_tronAimError, Vector3.up) * to.normalized;

            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            return Vector3.Angle(forward, m_tronAimDir) <= kTronFaceAngle;
        }

        //========================================
        // あおだる:暗転返し
        //========================================

        // 撃てたら true
        private bool UpdateBlackoutCounter(CpuContext c, Func<MatchPlayer, bool> engaged)
        {
            if (!MidaruCutIn.IsPlaying)
            {
                m_blackoutSeen = false;
                m_blackoutCounter = false;
                return false;
            }

            CpuParams p = c.Params;
            if (!m_blackoutSeen)
            {
                // 暗転した最初のフレームで決める(時間が止まっているので、位置も状態も暗転する前のまま)
                m_blackoutSeen = true;
                m_blackoutStart = Time.unscaledTime;
                m_blackoutCounter = false;
                if (!m_gauge.IsFull) return false;
                DarumaCharacter caster = FindFunnelCaster();
                if (caster == null || caster == c.Self) return false;
                if (!FacesCaster(c, caster, engaged)) return false;
                // 疲れているほど見逃す
                m_blackoutCounter = Random.value < p.BlackoutCounterChance * c.Alertness;
                if (LogDecisions) Debug.Log($"[CPU 必殺技] {c.Self.name} 暗転返し:向き合っていた → 返す={m_blackoutCounter}(余裕 {c.Alertness:0.00})");
            }

            if (!m_blackoutCounter) return false;
            if (Time.unscaledTime < m_blackoutStart + p.BlackoutCounterDelay) return false;
            // 撃てない間(ハンマーを振っているなど)は、暗転が終わるまで試し続ける
            if (!m_ability.TryActivate()) return false;
            m_blackoutCounter = false;
            BlackoutCounterCount++;
            return true;
        }

        // ファンネルを撃ったみだる(分裂弾を撃った人)
        private static DarumaCharacter FindFunnelCaster()
        {
            foreach (var block in TumikiBlock.All)
            {
                if (block == null) continue;
                var homing = block.GetComponent<TumikiHoming>();
                if (homing == null || !homing.IsHoming || homing.Shooter == null) continue;
                if (homing.Shooter.GetComponent<MidaruSpecial>() != null) return homing.Shooter;
            }
            return null;
        }

        // みだると向き合っていたか(人でも「ファンネルが自分に来る」と分かる場面か)
        //  ・みだると戦っていた(殴る・逃げる・撃つ・攻めに行く)
        //  ・みだるが明らかに近かった:みだるが自分に一番近い敵で、みだるから見ても自分がほかの敵より BlackoutNearMargin 以上近い
        private static bool FacesCaster(CpuContext c, DarumaCharacter caster, Func<MatchPlayer, bool> engaged)
        {
            CpuOpponent co = null;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.Character == caster)
                {
                    co = o;
                    break;
                }
            }
            if (co == null) return false;
            if (engaged != null && engaged(co.Player)) return true;

            float mine = CpuContext.FlatDistance(caster.transform.position, c.Position);
            foreach (var o in c.Perception.Opponents)
            {
                if (o == co || o.IsGhost || o.Character == null) continue;
                // 自分に一番近い敵がみだるでない
                if (o.FlatDistance < co.FlatDistance) return false;
                // みだるから見て、自分とほかの敵の近さがはっきりしない
                if (CpuContext.FlatDistance(caster.transform.position, o.Position) < mine + c.Params.BlackoutNearMargin) return false;
            }
            return true;
        }

        //========================================
        // あおだる:とっさの六腕
        //========================================

        // 撃てたら true
        private bool UpdateReflexArms(CpuContext c)
        {
            CpuParams p = c.Params;

            // 撃つと決めた:反応の遅れのあとに撃つ(撃てないまま時間がたったらあきらめる)
            if (m_armsPending)
            {
                if (Time.time < m_armsFireAt) return false;
                if (m_ability.TryActivate())
                {
                    m_armsPending = false;
                    return true;
                }
                if (Time.time > m_armsFireAt + kArmsGiveUp) m_armsPending = false;
                return false;
            }

            // 攻撃判定がなくなった弾は忘れる
            m_armsForget.Clear();
            foreach (var block in m_armsDecided)
            {
                if (block == null || !block.IsHarmful) m_armsForget.Add(block);
            }
            foreach (var block in m_armsForget) m_armsDecided.Remove(block);

            // 六腕が効き始める前に当たる弾には間に合わない
            float lead = p.ArmsLeadTime + p.ReflexReaction;
            m_armsThreats.Clear();
            bool fresh = false;
            foreach (var block in TumikiBlock.All)
            {
                if (block == null) continue;
                if (!CpuReflex.PredictHit(c, block, out float timeToHit) || timeToHit < lead) continue;
                m_armsThreats.Add(block);
                if (!m_armsDecided.Contains(block)) fresh = true;
            }
            // 新しく当たりそうになった弾があるときだけ決める(弾ごとに一度だけ)
            if (!fresh) return false;

            // 追い込まれている:段数が少なく、疲れていてジャンプ避けもままならない
            bool cornered = c.Self.Stack.Count <= p.ArmsPanicStack && c.LoadRatio >= p.ArmsPanicLoad;
            if (m_armsThreats.Count < 2 && !cornered) return false;
            foreach (var block in m_armsThreats) m_armsDecided.Add(block);

            // 気づくか(追い込まれているときは、疲れていても必死なので余裕を掛けない)
            float chance = cornered ? p.ReactChance : p.ReactChance * c.Alertness;
            bool notice = Random.value < chance;
            if (LogDecisions) Debug.Log($"[CPU 必殺技] {c.Self.name} とっさの六腕:弾 {m_armsThreats.Count} 発・段数 {c.Self.Stack.Count}・集中度 {c.LoadRatio:0.00}・追い込まれ {cornered} → 撃つ={notice}");
            if (!notice) return false;

            m_armsPending = true;
            m_armsFireAt = Time.time + p.ReflexReaction;
            return false;
        }
    }
}
