using System;
using System.Collections.Generic;
using UnityEngine;
using Yurube;
using Random = UnityEngine.Random;

namespace Nakahira
{
    // CPU が必殺技を撃つ(1人の CPU につき1つ)
    // ゲージが満タンで、そのキャラの発動条件(ShouldActivate)を満たしたら撃つ
    // キャラごとの撃ちどころは ShouldActivate(設計メモ 6.2.1)。だるきんぐはため中の向き直り・確定も AimExcalibur でする
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

        // 向き直り(あかだる・みだる):向き直っている最中か・狙う向き
        // あかだる(トロン):狙う相手・狙いのぶれ(度。向き直り始めに一度だけ決める)
        MatchPlayer m_tronTarget;
        bool m_aiming;
        Vector3 m_aimDir;
        float m_tronAimError;
        // 向き直るときにスティックを倒す強さ(歩かないよう小さく)
        const float kAimInput = 0.05f;
        // 狙う向きとの差がこれ(度)以内になったら撃つ
        const float kTronFaceAngle = 4.0f;

        // みだる(ファンネル):1 発目が向かう一番近い敵
        MatchPlayer m_funnelTarget;
        // いわだる(隕石):狙う敵(撃ったあとはカウントダウンの間ついていく)
        MatchPlayer m_meteorTarget;
        // へびだる(毒沼):狙う敵(一番段数が少ない敵)
        MatchPlayer m_swampTarget;
        // 敵の方との差がこれ(度)以内になったら撃つ(分裂弾は 1 秒に 360° 曲がって追う)
        const float kFunnelFaceAngle = 15.0f;
        // チーム戦:トロンの先頭の通り道に味方がいないか調べる長さ(狙う敵までの距離にこれを足す)
        const float kAllyLineBeyond = 3.0f;

        // だるきんぐ(エクスカリバー):ため中か・狙いのぶれ(度。ため始めに一度だけ決める)
        bool m_kingCharging;
        float m_kingAimError;
        // 状態「必殺技」で 2 人狙いを探した時間(秒。満タンの間は入り直しても続けて数える)と、1 人狙いで撃ったか
        float m_kingSeekTime;
        bool m_kingSolo;

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

            // だるきんぐ:ため中(ゲージは空)は、線に敵が入る向きへ向きを変え続け、中心部分に入ったら確定する
            if (m_ability is DaruKingSpecial king && king.IsCharging)
            {
                AimExcalibur(c, king);
                return false;
            }
            m_kingCharging = false;

            if (!m_gauge.IsFull)
            {
                m_wasFull = false;
                m_kingSeekTime = 0.0f;
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
            // みだる:一番近い敵の方へ向き直ってから撃つ(弾が曲がって追うので、あかだるより甘くてよい)
            if (!m_rash && m_ability is MidaruSpecial && !FacingFunnelTarget(c)) return false;
            // 撃てない間(ハンマーを振っているなど)は、撃ちどころが続く間 毎フレーム試す
            if (!m_ability.TryActivate()) return false;
            m_wasFull = false;
            // だるきんぐ:2 人狙いをあきらめて撃ったなら、ため中も 1 人入れば確定する
            m_kingSolo = KingSoloReady(c);
            m_kingSeekTime = 0.0f;
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
            m_aiming = false;
        }

        // 撃つ向きへ向き直っている最中なら、状態が出した操作を置き換える(状態の Tick のあとに呼ぶ)
        // 狙う向きへスティックを少しだけ倒す(歩かずに向きだけ変える)。ハンマー・ジャンプは止める
        public void OverrideAim(ref DarumaCommand cmd)
        {
            if (!m_aiming) return;
            cmd.Move = new Vector2(m_aimDir.x, m_aimDir.z) * kAimInput;
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
                m_kingSeekTime = 0.0f;
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
                case IwadaruSpecial iwa: return MeteorChance(c, iwa);   // 隕石
                case HebidaruSpecial hebi: return SwampChance(c, hebi);   // 毒沼
                case AkadaruSpecial _:  return TronChance(c);   // トロン(ビーム)
                case DaruKingSpecial king: return ExcaliburChance(c, king, KingSoloReady(c));   // エクスカリバー
                case MidaruSpecial _:   return FunnelChance(c);   // ファンネル
                case KidaruSpecial _:   return KidaruChance(c, current);   // 黄金化
                default:                return true;
            }
        }

        // だるきんぐ(エクスカリバー):向きを変えれば線(太さ全体)に 2 人入るとき(1 対 1 なら 1 人)
        // 状態「必殺技」で ExcaliburSoloAfter 秒たっても 2 人狙いが決まらなければ、1 人入れば撃つ(solo。2026-11-13 中平)
        // 撃つとため(2 秒)が始まり、その間に向きを決める(AimExcalibur)
        private static bool ExcaliburChance(CpuContext c, DaruKingSpecial king, bool solo)
        {
            int need = ExcaliburNeed(c);
            if (need <= 0) return false;
            if (solo) need = 1;
            BestExcaliburDir(c, king.AttackWidth * 0.5f, c.Params.ExcaliburConfirmHalfWidth, out _, out int full);
            return full >= need;
        }

        // ため中:線の中心部分に一番多く入る向き(2 人入らなければ 1 人)へ向きを変え続ける。
        // 今の向きの線の中心部分に 2 人(1 対 1 なら 1 人)入ったら確定を頼む(発動直後 0.15 秒は DaruKingSpecial が待つ)。
        // そうならなければ 2 秒でため切る。狙いのぶれは股抜きショットと同じ
        private void AimExcalibur(CpuContext c, DaruKingSpecial king)
        {
            if (!m_kingCharging)
            {
                m_kingCharging = true;
                float error = Mathf.Lerp(c.Params.ShotAimError, c.Params.ShotAimErrorMax, c.LoadRatio);
                m_kingAimError = Random.Range(-error, error);
            }
            float half = king.AttackWidth * 0.5f;
            float center = Mathf.Min(c.Params.ExcaliburConfirmHalfWidth, half);
            BestExcaliburDir(c, half, center, out Vector3 dir, out _);
            m_aiming = dir != Vector3.zero;   // 狙える敵がいなければ今の向きのまま
            if (m_aiming) m_aimDir = Quaternion.Euler(0.0f, m_kingAimError, 0.0f) * dir;

            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();
            int need = m_kingSolo ? 1 : Mathf.Max(1, ExcaliburNeed(c));
            if (CountOnLine(c, forward, center) < need) return;
            king.RequestConfirm();
            if (LogDecisions) Debug.Log($"[CPU 必殺技] {c.Self.name} エクスカリバー確定(中心部分に {need} 人以上)");
        }

        // 2 人狙いをあきらめて 1 人狙いにする時間がたったか
        private bool KingSoloReady(CpuContext c) => m_ability is DaruKingSpecial && m_kingSeekTime >= c.Params.ExcaliburSoloAfter;

        // 状態「必殺技」で近づいている間に呼ぶ(だるきんぐの 2 人狙いを探した時間を数える)
        public void TickApproach(CpuContext c)
        {
            if (!(m_ability is DaruKingSpecial)) return;
            float before = m_kingSeekTime;
            m_kingSeekTime += c.DeltaTime;
            if (LogDecisions && before < c.Params.ExcaliburSoloAfter && m_kingSeekTime >= c.Params.ExcaliburSoloAfter)
                Debug.Log($"[CPU 必殺技] {c.Self.name} エクスカリバー:2 人狙いが {c.Params.ExcaliburSoloAfter:0.#} 秒決まらない → 1 人狙い");
        }

        // 状態「必殺技」で向かう場所(target:近づく敵)
        //  ・だるきんぐ(2 人狙いの間):近い方の敵の、もう 1 人と反対側 ExcaliburLineupDistance m(そこから見ると 2 人が一直線に並ぶ)。床がなければ敵の所
        //  ・ほか:敵の所
        public Vector3 ApproachPoint(CpuContext c, CpuOpponent target)
        {
            if (!(m_ability is DaruKingSpecial) || KingSoloReady(c) || ExcaliburNeed(c) < 2) return target.Position;
            CpuOpponent other = null;
            float bestSqr = float.MaxValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (o == target || !IsExcaliburTarget(o)) continue;
                float sqr = (o.Position - target.Position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    other = o;
                }
            }
            if (other == null) return target.Position;
            Vector3 away = target.Position - other.Position;
            away.y = 0.0f;
            if (away.sqrMagnitude < 0.0001f) return target.Position;
            Vector3 point = target.Position + away.normalized * c.Params.ExcaliburLineupDistance;
            return CpuSensor.HasGround(point + Vector3.up, 3.0f) ? point : target.Position;
        }

        // 線に入れたい人数:自分以外の生存者(幽霊・復活待ちを除く)が 1 人だけなら 1、2 人以上なら 2、いなければ 0
        private static int ExcaliburNeed(CpuContext c)
        {
            int alive = 0;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost || o.Character == null) continue;
                alive++;
            }
            return Mathf.Min(alive, 2);
        }

        // 自分から dir の向きの線の、中心から左右 halfWidth 以内にいる敵(無敵でない)の数
        private static int CountOnLine(CpuContext c, Vector3 dir, float halfWidth)
        {
            Vector3 pos = c.Position;
            int count = 0;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost || o.IsInvincible || o.Character == null) continue;
                Vector3 d = o.Position - pos;
                d.y = 0.0f;
                float along = Vector3.Dot(d, dir);
                if (along < 0.0f) continue;
                if ((d - dir * along).sqrMagnitude <= halfWidth * halfWidth) count++;
            }
            return count;
        }

        // 線に一番多く敵が入る向き。候補は「敵 1 人の方」と「敵 2 人の真ん中の方」(2 人から線までの距離がそろう)
        // 中心部分(center)に入る数が多い順 → 太さ全体(half)に入る数が多い順 → 今の向きから回る角度が小さい順。返り値は中心部分の数
        private static int BestExcaliburDir(CpuContext c, float half, float center, out Vector3 best, out int bestFull)
        {
            best = Vector3.zero;
            bestFull = 0;
            int bestCenter = -1;
            float bestAngle = float.MaxValue;
            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            Vector3 pos = c.Position;
            var list = c.Perception.Opponents;
            for (int i = 0; i < list.Count; i++)
            {
                if (!IsExcaliburTarget(list[i])) continue;
                for (int j = i; j < list.Count; j++)
                {
                    if (!IsExcaliburTarget(list[j])) continue;
                    Vector3 d = j == i ? list[i].Position - pos : (list[i].Position - pos) + (list[j].Position - pos);
                    d.y = 0.0f;
                    if (d.sqrMagnitude < 0.0001f) continue;
                    d.Normalize();
                    int inCenter = CountOnLine(c, d, center);
                    int inFull = CountOnLine(c, d, half);
                    float angle = Vector3.Angle(forward, d);
                    bool better = inCenter > bestCenter
                        || (inCenter == bestCenter && (inFull > bestFull || (inFull == bestFull && angle < bestAngle)));
                    if (!better) continue;
                    best = d;
                    bestCenter = inCenter;
                    bestFull = inFull;
                    bestAngle = angle;
                }
            }
            return Mathf.Max(0, bestCenter);
        }

        private static bool IsExcaliburTarget(CpuOpponent o) => o != null && !o.IsGhost && !o.IsInvincible && o.Character != null;

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
            if (!m_aiming)
            {
                // 狙いのぶれは股抜きショットと同じ:集中度の割合が高いほど大きい
                float error = Mathf.Lerp(c.Params.ShotAimError, c.Params.ShotAimErrorMax, c.LoadRatio);
                m_tronAimError = Random.Range(-error, error);
                m_aiming = true;
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
            m_aimDir = Quaternion.AngleAxis(m_tronAimError, Vector3.up) * to.normalized;
            // チーム戦:トロンの先頭の積み木の通り道に味方がいる間は撃たない(先頭は普通の弾と同じく味方にも当たる)
            if (c.AllyInLine(pos, m_aimDir, to.magnitude + kAllyLineBeyond)) { m_aiming = false; return false; }   // 向き直りもやめて、状態の動きに戻す

            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            return Vector3.Angle(forward, m_aimDir) <= kTronFaceAngle;
        }

        // 状態「必殺技」(撃てる所まで近づく)に入りたいか:ゲージが満タンで、近づかないと撃てないキャラ(今はみだる)
        // 射程の外でも、満タンなら近づきに行く(撃ちどころを見逃していても入れる)
        public bool WantsApproach(CpuContext c)
        {
            if (Disabled || m_gauge == null || !m_gauge.IsFull) return false;
            // へびだるの毒沼・だるきんぐのエクスカリバーは積み木 0 個でも撃てる(ほかは 1 個以上)
            // だるきんぐは敵 2 人が一直線に並ぶ位置へ回り込む(ApproachPoint。2026-11-13 中平)
            if (m_ability is HebidaruSpecial || m_ability is DaruKingSpecial) return true;
            if (c.StackCount <= 0) return false;
            return m_ability is MidaruSpecial || m_ability is IwadaruSpecial;
        }

        // 撃ったあと、敵についていく最中か
        //  ・いわだる:カウントダウン中(円が自分についてくる)
        //  ・へびだる:毒沼が出ている間(沼が自分についてくる。反転中の敵を追って殴る)
        public bool IsFollowing
        {
            get
            {
                if (Disabled) return false;
                if (m_ability is IwadaruSpecial iwa) return iwa.CountdownRemaining > 0.0f;
                if (m_ability is HebidaruSpecial hebi) return hebi.IsActive;
                return false;
            }
        }
        // へびだるの毒沼が出ている間か(近づいたら殴るに引き継ぐ)
        public bool IsSwampFollowing => IsFollowing && m_ability is HebidaruSpecial;

        // ついていく敵(いなければ null)。current:今ついていっている敵
        //  ・いわだる:撃ったときに狙っていた敵
        //  ・へびだる:反転している敵のうち道で一番近い敵。いなければ一番段数が少ない敵
        public CpuOpponent FollowTarget(CpuContext c, MatchPlayer current)
        {
            if (m_ability is IwadaruSpecial)
            {
                CpuOpponent o = c.Perception.Find(m_meteorTarget);
                return IsReachable(o) ? o : ApproachTarget(c, current);
            }
            CpuOpponent best = null;
            float bestScore = float.MinValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (!IsReachable(o) || !c.IsTrapped(o)) continue;
                float score = -o.PathDistance + (o.Player == current ? 1.0f : 0.0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best ?? ApproachTarget(c, current);
        }

        // 近づける敵か(生存者・無敵でない・歩いて行ける)
        private static bool IsReachable(CpuOpponent o)
        {
            if (o == null || o.IsGhost || o.IsInvincible || o.Character == null) return false;
            return !float.IsPositiveInfinity(o.PathDistance);
        }

        // 状態「必殺技」で近づく敵(いなければ null)。current:今近づいている敵(少し優先する)
        //  ・みだる:道で一番近い敵
        //  ・いわだる:一番積み木を積んでいる(一番遅い)敵。同じなら道で近い方(確実に倒せれば一番有利なので。中平が決めた)
        //  ・へびだる:一番段数が少ない敵(首だけで逃げている敵を捕まえる。中平が決めた)。同じなら道で近い方
        public CpuOpponent ApproachTarget(CpuContext c, MatchPlayer current)
        {
            bool meteor = m_ability is IwadaruSpecial;
            bool swamp = m_ability is HebidaruSpecial;
            CpuOpponent best = null;
            float bestScore = float.MinValue;
            foreach (var o in c.Perception.Opponents)
            {
                if (!IsReachable(o)) continue;
                // 1 段 = 道 100m 分。いわだるは段数が大きいほど、へびだるは小さいほど、みだるは道が近いほど高い
                float stackScore = meteor ? o.StackCount * 100.0f : swamp ? -o.StackCount * 100.0f : 0.0f;
                float score = stackScore - o.PathDistance;
                if (o.Player == current) score += meteor || swamp ? 50.0f : 1.0f;   // 今の相手を少し優先(ころころ変えない)
                if (score > bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }
            return best;
        }

        // へびだる(毒沼):狙う敵(一番段数が少ない敵)が「今撃ったときの沼の半径 + SwampFireMargin」以内に入ったとき。
        // 入ってから撃つのではなく、撃ちながらにじり寄って沼に飲み込む(壁際に追い詰める・首だけで逃げる敵を捕まえる。中平が決めた)
        private bool SwampChance(CpuContext c, HebidaruSpecial hebi)
        {
            CpuOpponent target = ApproachTarget(c, m_swampTarget);
            m_swampTarget = target != null ? target.Player : null;
            if (target == null) return false;
            return target.FlatDistance <= hebi.PredictRadius(c.StackCount) + c.Params.SwampFireMargin;
        }

        // いわだる(隕石):狙う敵(一番積み木を積んでいる敵)が、今撃ったときの円の半径 × MeteorFireRadiusRatio 以内にいるとき
        private bool MeteorChance(CpuContext c, IwadaruSpecial iwa)
        {
            if (c.StackCount <= 0) return false;
            CpuOpponent target = ApproachTarget(c, m_meteorTarget);
            m_meteorTarget = target != null ? target.Player : null;
            if (target == null) return false;
            // 自分の方が速い相手なら、カウントダウンの間に追いついて重なれるので、円の中に入っていれば撃つ
            bool faster = c.Self.MoveSpeed > target.Character.MoveSpeed;
            float ratio = faster ? c.Params.MeteorFastFireRadiusRatio : c.Params.MeteorFireRadiusRatio;
            return target.FlatDistance <= iwa.PredictRadius(c.StackCount) * ratio;
        }

        // みだる(ファンネル):どの状態の最中でも、一番近い敵(1 発目が向かう敵)が FunnelRange 以内で、間に壁がないとき
        // (分裂弾は近い敵から順に割り振られる。一番近い敵が幽霊でなく無敵でもないことも見る)
        private bool FunnelChance(CpuContext c)
        {
            m_funnelTarget = null;
            if (c.StackCount <= 0) return false;
            CpuOpponent nearest = null;
            foreach (var o in c.Perception.Opponents)
            {
                if (o.IsGhost || o.Character == null) continue;
                if (nearest == null || o.FlatDistance < nearest.FlatDistance) nearest = o;
            }
            if (nearest == null || nearest.IsInvincible) return false;
            if (nearest.FlatDistance > c.Params.FunnelRange) return false;
            if (CpuShotState.HasWall(c.Position, nearest.Position)) return false;
            m_funnelTarget = nearest.Player;
            return true;
        }

        // 一番近い敵の方へ向き直ったか
        private bool FacingFunnelTarget(CpuContext c)
        {
            CpuOpponent o = m_funnelTarget != null ? c.Perception.Find(m_funnelTarget) : null;
            if (o == null) return false;
            Vector3 to = o.Position - c.Position;
            to.y = 0.0f;
            if (to.sqrMagnitude < 0.0001f) return true;
            m_aiming = true;
            m_aimDir = to.normalized;
            // チーム戦:撃ち出す向き(一番近い敵まで)に味方がいる間は撃たない(分裂弾は普通の弾と同じく味方にも当たる)
            if (c.AllyInLine(c.Position, m_aimDir, to.magnitude)) { m_aiming = false; return false; }   // 向き直りもやめて、状態の動きに戻す
            Vector3 forward = c.Self.transform.forward;
            forward.y = 0.0f;
            return Vector3.Angle(forward, m_aimDir) <= kFunnelFaceAngle;
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
                // チーム戦:味方のみだるのファンネルは自分に来ない
                if (DarumaMatch.Current != null && DarumaMatch.Current.IsFriendly(caster, c.Self)) return false;
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
