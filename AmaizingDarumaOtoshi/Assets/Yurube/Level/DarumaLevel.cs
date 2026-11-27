using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using Yurube.Achievements;

namespace Yurube
{
    // レベル（2026-11-24 メンバー全員で決めた、パッシブの新しい仕様）。値は LevelParams。
    //  ・試合中に積んだ数（上から乗って拾った数の合計）でレベルが上がる。最大 Lv5。やられても下がらない（プレイヤーごとに覚える）
    //  ・全員共通：攻撃速度・移動速度・弾速が上がる
    //  ・Lv2 から、キャラごとの能力（CharacterData.levelPerk）も上がる
    //      あかだる 弾速 / あおだる 攻撃速度 / みだる 当てた相手の移動速度ダウン / きだる スタミナ /
    //      いわだる 撃った弾の耐久値 / へびだる 撃った弾の蛇行の振れ幅 / だるきんぐ リーチ
    // 前のパッシブ（色の積み木で強くなる・きだるの増殖・みだるのホーミング・いわだるの岩・へびだるの蛇行・だるきんぐの2段）は
    // LegacyPassives が false のあいだ止めてある（各パッシブのスクリプトがこの値を見る）。必殺技はそのまま。
    // 試合が始まると自動で1つ作られる（シーンやプレハブに置かなくてよい）。ネット対戦ではホストだけが計算する。
    [DefaultExecutionOrder(-90)]   // DarumaCharacter の FixedUpdate より先に速度の倍率を渡す
    public class DarumaLevel : MonoBehaviour
    {
        // 前のパッシブを使うか（false：新しいレベルの仕様。true に戻すと前のパッシブが動き、レベルは止まる）
        public static bool LegacyPassives = false;

        // レベルが上がった（演出用）。プレイヤーと新しいレベル
        public static event Action<MatchPlayer, int> LevelUp;

        static DarumaLevel s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LevelUp = null;
            s_instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("DarumaLevel");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<DarumaLevel>();
        }

        // そのプレイヤーの今のレベル（試合が無い・知らないプレイヤーなら 1）
        public static int LevelOf(MatchPlayer p) =>
            s_instance != null && p != null && s_instance.m_states.TryGetValue(p, out var s) ? s.level : 1;
        // そのプレイヤーが試合中に積んだ数の合計
        public static int StackedOf(MatchPlayer p) =>
            s_instance != null && p != null && s_instance.m_states.TryGetValue(p, out var s) ? s.stacked : 0;

        class State
        {
            public int stacked;
            public int level = 1;
            // ラボ用: 試合外で使うキャラデータ
            public CharacterData labData;
            // 今の体と、その体のハンマーの性能（このプレイヤー用の複製と、複製したときの元の値）
            public DarumaCharacter body;
            public DarumaHammer hammer;
            public HammerParams clone;
            public float baseSweep, baseShot, baseShotSpeed, baseLaunch, baseMaxSpeed, baseReach;
            // みだるに当てられて遅くなっている（終わる時刻と倍率）
            public float slowUntil;
            public float slowScale = 1f;
        }

        readonly Dictionary<MatchPlayer, State> m_states = new Dictionary<MatchPlayer, State>();
        readonly Dictionary<DarumaCharacter, State> m_labStates = new Dictionary<DarumaCharacter, State>();
        readonly List<DarumaCharacter> m_labDeadKeys = new List<DarumaCharacter>();
        DarumaMatch m_match;

        static DarumaLevel EnsureInstance()
        {
            if (s_instance == null) Boot();
            return s_instance;
        }

        // ---- ラボ用(試合外 DarumaMatch.Current == null のときだけ動く) ----
        public static void RegisterLab(DarumaCharacter body, CharacterData data, int level = 1)
        {
            if (body == null || DarumaMatch.Current != null) return;
            var inst = EnsureInstance();
            if (inst == null) return;
            if (!inst.m_labStates.TryGetValue(body, out var s))
            {
                s = new State();
                inst.m_labStates[body] = s;
            }
            s.labData = data;
            ApplyLevelToState(s, level);
            if (Active) inst.UpdateSingleState(body, s, data);
        }

        public static void UnregisterLab(DarumaCharacter body)
        {
            if (s_instance == null || body == null) return;
            s_instance.m_labStates.Remove(body);
        }

        public static void ClearLab()
        {
            if (s_instance == null) return;
            s_instance.m_labStates.Clear();
        }

        public static void SetLabLevel(DarumaCharacter body, int level)
        {
            if (body == null || DarumaMatch.Current != null) return;
            var inst = EnsureInstance();
            if (inst == null || !inst.m_labStates.TryGetValue(body, out var s)) return;
            ApplyLevelToState(s, level);
            if (Active) inst.UpdateSingleState(body, s, s.labData);
        }

        public static int GetLabLevel(DarumaCharacter body) =>
            s_instance != null && body != null && s_instance.m_labStates.TryGetValue(body, out var s) ? s.level : 1;

        public static int GetLabStacked(DarumaCharacter body) =>
            s_instance != null && body != null && s_instance.m_labStates.TryGetValue(body, out var s) ? s.stacked : 0;

        public static bool IsLabSlowed(DarumaCharacter body) =>
            s_instance != null && body != null && s_instance.m_labStates.TryGetValue(body, out var s) && Time.time < s.slowUntil;

        public static float GetLabSlowScale(DarumaCharacter body) =>
            s_instance != null && body != null && s_instance.m_labStates.TryGetValue(body, out var s) && Time.time < s.slowUntil ? s.slowScale : 1f;

        public static void ComputeMultipliers(int level, LevelPerk perk, float weaponReach,
            out float attackMul, out float shotMul, out float moveMul, out float reachMul, out float staminaEff)
        {
            var lp = LevelParams.Instance;
            int n = PerkSteps(level);
            attackMul = 1f + lp.attackSpeedPerLevel * n;
            shotMul = 1f + lp.shotSpeedPerLevel * n;
            moveMul = 1f + lp.moveSpeedPerLevel * n;
            float reach = 1f;
            if (perk == LevelPerk.AttackSpeed) attackMul *= 1f + lp.perkAttackSpeed * n;
            if (perk == LevelPerk.ShotSpeed) shotMul *= 1f + lp.perkShotSpeed * n;
            if (perk == LevelPerk.Reach) reach *= 1f + lp.perkReach * n;
            reachMul = Mathf.Max(0.5f, weaponReach) * reach;
            staminaEff = perk == LevelPerk.Stamina ? 1f + lp.perkStamina * n : 1f;
        }

        static void ApplyLevelToState(State s, int level)
        {
            level = Mathf.Clamp(level, 1, LevelParams.MaxLevel);
            s.level = level;
            int[] need = LevelParams.Instance.stackedToLevel;
            s.stacked = (level >= 2 && need != null && level - 2 < need.Length) ? need[level - 2] : 0;
        }

        void OnEnable()
        {
            DarumaCharacter.AnyPickedUp += OnPickedUp;
            DarumaHammer.ShotFired += OnShotFired;
            DarumaHammer.Hit += OnHammerHit;
            TumikiBlock.Hit += OnBlockHit;
        }

        void OnDisable()
        {
            DarumaCharacter.AnyPickedUp -= OnPickedUp;
            DarumaHammer.ShotFired -= OnShotFired;
            DarumaHammer.Hit -= OnHammerHit;
            TumikiBlock.Hit -= OnBlockHit;
            if (m_match != null) m_match.Started -= OnMatchStarted;
        }

        static bool Active => !LegacyPassives && !SpecialNet.IsClient;

        // 試合が変わった・やり直した（同じシーンでもう一度）ときは、全員 Lv1 から
        void WatchMatch()
        {
            var match = DarumaMatch.Current;
            if (match == m_match) return;
            if (m_match != null) m_match.Started -= OnMatchStarted;
            m_match = match;
            m_states.Clear();
            if (m_match != null)
            {
                m_labStates.Clear();
                m_match.Started += OnMatchStarted;
            }
        }

        void OnMatchStarted() => m_states.Clear();

        State StateOf(MatchPlayer p)
        {
            if (!m_states.TryGetValue(p, out var s)) m_states[p] = s = new State();
            return s;
        }

        MatchPlayer PlayerOf(Component who)
        {
            if (m_match == null || who == null) return null;
            foreach (var p in m_match.Players)
                if (p != null && ((p.Character != null && p.Character == who) || (p.Ghost != null && p.Ghost == who))) return p;
            return null;
        }

        // ---------------- 積んだ数 → レベル ----------------
        void OnPickedUp(TumikiPickupInfo info)
        {
            WatchMatch();
            if (!Active || !info.StackGrew) return;
            if (m_match == null)
            {
                if (info.Character != null && m_labStates.TryGetValue(info.Character, out var ls))
                {
                    ls.stacked++;
                    int next = LevelParams.Instance.LevelFor(ls.stacked);
                    if (next > ls.level)
                    {
                        ls.level = next;
                        Debug.Log($"[ラボ・レベル] {(ls.labData != null ? ls.labData.displayName : info.Character.name)} Lv{next}（積んだ数 {ls.stacked}）");
                    }
                }
                return;
            }
            var p = PlayerOf(info.Character);
            if (p == null) return;
            var s = StateOf(p);
            s.stacked++;
            int level = LevelParams.Instance.LevelFor(s.stacked);
            if (level <= s.level) return;
            s.level = level;
            Debug.Log($"[レベル] {p.Label}（{(p.CharacterData != null ? p.CharacterData.displayName : "-")}）Lv{level}（積んだ数 {s.stacked}）");
            // キャラごとの能力が動き出した（Lv2）＝パッシブが発動した、として実績に知らせる
            if (level >= 2 && Perk(p) != LevelPerk.None && p.Character != null) AchievementEvents.RaisePassiveTriggered(p.Character);
            LevelUp?.Invoke(p, level);
        }

        // デバッグ用(suehara): そのプレイヤーをすぐ最大レベルにする(LT のデバッグボタンから呼ぶ)。実績には数えない
        public static bool DebugSetMaxLevel(MatchPlayer p)
        {
            if (s_instance == null || p == null) return false;
            s_instance.WatchMatch();
            if (!Active || s_instance.PlayerOf(p.Character) != p) return false;
            var s = s_instance.StateOf(p);
            if (s.level >= LevelParams.MaxLevel) return false;
            int[] need = LevelParams.Instance.stackedToLevel;
            if (need != null && need.Length > 0) s.stacked = Mathf.Max(s.stacked, need[need.Length - 1]);
            s.level = LevelParams.MaxLevel;
            LevelUp?.Invoke(p, s.level);
            return true;
        }

        // デバッグ用(Fujihara): そのプレイヤーのレベルを delta だけ上げ下げする(LT で -1・RT で +1。SpecialGaugeDebugButton から呼ぶ)。実績には数えない
        //  積んだ数も、そのレベルになる最小の数にそろえる(そのあと積めば、ふつうに続きから上がる)
        public static bool DebugAddLevel(MatchPlayer p, int delta)
        {
            if (s_instance == null || p == null || delta == 0) return false;
            s_instance.WatchMatch();
            if (!Active || s_instance.PlayerOf(p.Character) != p) return false;
            var s = s_instance.StateOf(p);
            int level = Mathf.Clamp(s.level + delta, 1, LevelParams.MaxLevel);
            if (level == s.level) return false;
            int[] need = LevelParams.Instance.stackedToLevel;
            s.stacked = level >= 2 && need != null && need.Length >= level - 1 ? need[level - 2] : 0;
            s.level = level;
            if (delta > 0) LevelUp?.Invoke(p, s.level);
            return true;
        }

        static LevelPerk Perk(MatchPlayer p) => p != null && p.CharacterData != null ? p.CharacterData.levelPerk : LevelPerk.None;
        static LevelPerk PerkOf(CharacterData data) => data != null ? data.levelPerk : LevelPerk.None;

        // Lv2 から数えた、キャラごとの能力の段階（Lv1 なら 0、Lv5 なら 4）
        static int PerkSteps(int level) => Mathf.Max(0, level - 1);

        // ---------------- 毎フレーム：ハンマーの性能・スタミナ ----------------
        void Update()
        {
            WatchMatch();
            if (!Active) return;
            if (m_match != null)
            {
                foreach (var p in m_match.Players)
                {
                    if (p == null || p.Character == null) continue;
                    UpdateSingleState(p.Character, StateOf(p), p.CharacterData);
                }
                return;
            }

            if (m_labStates.Count == 0) return;
            CleanupDeadLabStates();
            foreach (var pair in m_labStates)
                UpdateSingleState(pair.Key, pair.Value, pair.Value.labData);
        }

        void UpdateSingleState(DarumaCharacter body, State s, CharacterData data)
        {
            if (body == null) return;
            AdoptBody(body, s);
            if (s.clone == null) return;

            var perk = PerkOf(data);
            float weapon = data != null ? data.weaponReach : 1f;
            ComputeMultipliers(s.level, perk, weapon, out float attack, out float shot, out _, out float reachMul, out float staminaEff);

            s.clone.SweepDuration = s.baseSweep / attack;
            s.clone.ShotDuration = s.baseShot / attack;
            s.clone.ShotSpeed = s.baseShotSpeed * shot;
            s.clone.SweepLaunchSpeed = s.baseLaunch * shot;
            s.clone.MaxProjectileSpeed = s.baseMaxSpeed * shot;
            s.clone.HitReachScale = s.baseReach * reachMul;

            body.StaminaEfficiency = staminaEff;
        }

        // 体が新しくなった（復活）・ハンマーの性能をほかの人に差し替えられたら、今の性能を元にして複製し直す
        void AdoptBody(DarumaCharacter body, State s)
        {
            var hammer = body.Hammer != null ? body.Hammer : body.GetComponentInChildren<DarumaHammer>(true);
            if (hammer == null || hammer.Params == null) { s.clone = null; return; }
            if (body == s.body && hammer == s.hammer && hammer.Params == s.clone) return;
            s.body = body;
            s.hammer = hammer;
            var src = hammer.Params;
            s.baseSweep = src.SweepDuration;
            s.baseShot = src.ShotDuration;
            s.baseShotSpeed = src.ShotSpeed;
            s.baseLaunch = src.SweepLaunchSpeed;
            s.baseMaxSpeed = src.MaxProjectileSpeed;
            s.baseReach = src.HitReachScale;
            s.clone = Instantiate(src);
            s.clone.name = src.name + "(Level)";
            hammer.Params = s.clone;
        }

        // ---------------- 物理のステップ：移動速度 ----------------
        void FixedUpdate()
        {
            if (!Active) return;
            var lp = LevelParams.Instance;
            if (m_match != null)
            {
                foreach (var p in m_match.Players)
                {
                    if (p == null || p.Character == null || !p.Character.CanBeAffected) continue;
                    ApplySpeedScale(p.Character, StateOf(p), lp);
                }
                return;
            }

            if (m_labStates.Count == 0) return;
            CleanupDeadLabStates();
            foreach (var pair in m_labStates)
            {
                if (pair.Key != null && pair.Key.CanBeAffected) ApplySpeedScale(pair.Key, pair.Value, lp);
            }
        }

        static void ApplySpeedScale(DarumaCharacter body, State s, LevelParams lp)
        {
            float scale = 1f + lp.moveSpeedPerLevel * PerkSteps(s.level);
            if (Time.time < s.slowUntil) scale *= s.slowScale;
            if (Mathf.Abs(scale - 1f) > 0.0001f) body.RequestSpeedScale(scale);
        }

        void CleanupDeadLabStates()
        {
            m_labDeadKeys.Clear();
            foreach (var pair in m_labStates)
                if (pair.Key == null) m_labDeadKeys.Add(pair.Key);
            for (int i = 0; i < m_labDeadKeys.Count; i++)
                m_labStates.Remove(m_labDeadKeys[i]);
        }

        // ---------------- 撃った弾：いわだるの耐久値・へびだるの蛇行 ----------------
        void OnShotFired(HammerHitInfo info)
        {
            if (!Active) return;
            var block = info.Target as TumikiBlock;
            var shooter = info.Owner as DarumaCharacter;
            if (block == null || shooter == null) return;

            int level;
            LevelPerk perk;
            string label;
            if (m_match != null)
            {
                var p = PlayerOf(shooter);
                if (p == null) return;
                level = StateOf(p).level;
                perk = Perk(p);
                label = p.Label;
            }
            else
            {
                if (!m_labStates.TryGetValue(shooter, out var ls)) return;
                level = ls.level;
                perk = PerkOf(ls.labData);
                label = ls.labData != null ? ls.labData.displayName : shooter.name;
            }

            int n = PerkSteps(level);
            if (n <= 0) return;
            var lp = LevelParams.Instance;
            switch (perk)
            {
                case LevelPerk.ShotDurability:
                    var keep = block.GetComponent<LevelShotDurability>();
                    if (keep == null) keep = block.gameObject.AddComponent<LevelShotDurability>();
                    keep.Begin(lp.perkShotDurability * n);
                    Debug.Log($"[レベル] {label}（いわだる）Lv{n + 1} の弾：耐久値 +{lp.perkShotDurability * n}");
                    break;
                case LevelPerk.Zigzag:
                    var zig = block.GetComponent<TumikiZigzag>();
                    if (zig == null) zig = block.gameObject.AddComponent<TumikiZigzag>();
                    float angle = lp.perkZigzagAngleBase + lp.perkZigzagAnglePerLevel * (n - 1);
                    zig.BeginSharp(shooter, lp.perkZigzagInterval, angle);
                    Debug.Log($"[レベル] {label}（へびだる）Lv{n + 1} の弾：ジグザグ ±{angle:0}度");
                    break;
            }
        }

        // ---------------- 当てた：みだるの移動速度ダウン ----------------
        void OnHammerHit(HammerHitInfo info)
        {
            if (info.Damaged) TrySlow(info.Owner as Component, info.Target as DarumaCharacter);
        }

        void OnBlockHit(TumikiHitInfo info)
        {
            if (info.IsHarmful && info.Block != null) TrySlow(info.Block.Owner as Component, info.Target as DarumaCharacter);
        }

        // みだるのマシンガンの弾など(suehara): レベルが足りなくても、パッシブの「当てた相手を遅くする」を
        // Lv2 と同じ強さ以上でかける(Lv3 以上ならそのレベルの強さ)
        public static void ApplySlowAtLeastOneStep(DarumaCharacter attacker, DarumaCharacter victim)
        {
            if (s_instance == null) return;
            s_instance.WatchMatch();
            s_instance.TrySlow(attacker, victim, 1);
        }

        // minSteps: レベルが足りなくても使う、能力の最低の段階(ふだんは 0。suehara)
        void TrySlow(Component attacker, DarumaCharacter victim, int minSteps = 0)
        {
            if (!Active || attacker == null || victim == null) return;

            State vs;
            int attackerLevel;
            LevelPerk attackerPerk;
            string attackerLabel;
            string victimLabel;

            if (m_match != null)
            {
                var a = PlayerOf(attacker);
                var v = PlayerOf(victim);
                if (a == null || v == null || a == v) return;
                if (m_match.IsFriendly(attacker as IDarumaAttacker, victim)) return;
                vs = StateOf(v);
                attackerLevel = StateOf(a).level;
                attackerPerk = Perk(a);
                attackerLabel = a.Label;
                victimLabel = v.Label;
            }
            else
            {
                var attackerChar = attacker as DarumaCharacter ?? attacker.GetComponentInParent<DarumaCharacter>();
                if (attackerChar == null || attackerChar == victim) return;
                if (!m_labStates.TryGetValue(attackerChar, out var la) || !m_labStates.TryGetValue(victim, out vs)) return;
                attackerLevel = la.level;
                attackerPerk = PerkOf(la.labData);
                attackerLabel = la.labData != null ? la.labData.displayName : attackerChar.name;
                victimLabel = victim.name;
            }

            if (attackerPerk != LevelPerk.SlowOnHit) return;
            int n = Mathf.Max(minSteps, PerkSteps(attackerLevel));
            if (n <= 0) return;
            var lp = LevelParams.Instance;
            vs.slowScale = Mathf.Clamp(1f - lp.perkSlow * n, 0.1f, 1f);
            vs.slowUntil = Time.time + lp.perkSlowSeconds;
            Debug.Log($"[レベル] {attackerLabel}（みだる）Lv{n + 1} が {victimLabel} に当てた：移動速度 ×{vs.slowScale:0.00}（{lp.perkSlowSeconds:0.0}秒）");
        }

        // みだるに当てられて遅くなっているか（見た目用）
        public static bool IsSlowed(MatchPlayer p) =>
            s_instance != null && p != null && s_instance.m_states.TryGetValue(p, out var s) && Time.time < s.slowUntil;
    }
}
