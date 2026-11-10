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
        }

        // 撃てたら true。engaged:その相手と戦っている最中か(殴る・逃げる・撃つ・攻めに行く)
        // 時間が止まっている間(カットイン中)も毎フレーム呼ばれる
        public bool Apply(CpuContext c, Func<MatchPlayer, bool> engaged)
        {
            if (Disabled) return false;
            Bind(c.Self);
            if (m_ability == null || m_gauge == null) return false;

            // 暗転返しは時間が止まっている間に撃つので、下の「時間が止まっている間は撃たない」より先に見る
            if (m_ability is AodaruSpecial && UpdateBlackoutCounter(c, engaged)) return Activated();

            if (!m_gauge.IsFull) return false;
            // 時間が止まっている・遅くなっている間(ほかの必殺技のカットイン・ヒットストップ・やられ演出)は撃たない
            if (Time.timeScale < DarumaMatch.PlaySpeed) return false;

            if (m_ability is AodaruSpecial) return UpdateReflexArms(c) && Activated();

            if (!ShouldActivate(c)) return false;
            if (!m_ability.TryActivate()) return false;
            return Activated();
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
            }
            if (m_self == null) return;
            if (m_ability == null) m_ability = m_self.GetComponent<ISpecialAbility>();
            if (m_gauge == null) m_gauge = m_self.GetComponent<SpecialGauge>();
        }

        // キャラごとの発動条件(ゲージが満タンのときだけ呼ばれる。あおだるは別)
        private bool ShouldActivate(CpuContext c)
        {
            switch (m_ability)
            {
                case IwadaruSpecial _:  return true;   // 隕石
                case HebidaruSpecial _: return true;   // 毒沼
                case AkadaruSpecial _:  return true;   // トロン(ビーム)
                case DaruKingSpecial _: return true;   // エクスカリバー
                case MidaruSpecial _:   return true;   // ファンネル
                case KidaruSpecial _:   return true;   // 黄金化
                default:                return true;
            }
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
