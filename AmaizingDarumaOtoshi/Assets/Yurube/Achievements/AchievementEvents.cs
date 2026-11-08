using System;
using Nakahira;
using UnityEngine;

namespace Yurube.Achievements
{
    // ゲーム本編と実績システムの間の「お知らせ」。
    // 攻撃を当てた・勝ったなどの出来事は、まずここへ流す。受け取るのは AchievementManager。
    // ゲーム側（AchievementTracker）も実績ラボも、同じこの入り口から通知するので、
    // 「実際のゲームで解除される条件」と「ラボで試す条件」が必ず同じ道を通る。
    public static class AchievementEvents
    {
        // 試合が始まった（1試合ぶんの進捗をリセットする）
        public static event Action MatchStarted;
        // 試合が終わった（won: 人間が勝ったか / lowStackWin: 積み木1個以下で勝ったか）
        public static event Action<bool, bool> MatchFinished;
        // 攻撃が相手に当たった（count: 当たった数。ふつうは1）
        public static event Action<int> Hit;
        // カウンター・打ち返しに成功した
        public static event Action Counter;
        // カウンター・打ち返しで相手を撃破した
        public static event Action CounterKO;
        // 積み木の連鎖が起きた（length: 連鎖した積み木の数）
        public static event Action<int> Chain;
        // 積み木を積んだ（count: いま積んでいる数）
        public static event Action<int> Stack;
        // 必殺技を発動した
        public static event Action Special;
        // キャラクターを使った（CharacterData.id）
        public static event Action<string> CharacterUsed;

        // ---- 追加実績用 ----
        // 敗北した（CPU が勝った。引き分けは含めない）
        public static event Action MatchLost;
        // いずれかのパッシブが発動した（人物かどうかは AchievementTracker が判定する）
        public static event Action<DarumaCharacter> PassiveTriggered;
        // 人間のパッシブが発動した（AchievementManager が数える）
        public static event Action PassiveActivated;
        // いまの山に積まれている積み木の種類数（最大値を記録する）
        public static event Action<int> BlockTypesInStack;
        // 同じ積み木を打ち返したラリー数（最大値を記録する）
        public static event Action<int> Rally;
        // 他の人に傷つけられた相手を撃破した
        public static event Action OpportunistKO;
        // 連鎖によって相手を撃破した
        public static event Action ChainKO;
        // 同時に撃破した人数（最大値を記録する）
        public static event Action<int> MultiKO;
        // 一度も被弾せずに勝利した
        public static event Action NoDamageWin;
        // ハンマーを空振りした
        public static event Action HammerMiss;
        // 試合開始から一定時間以内に勝利した
        public static event Action QuickWin;

        public static void RaiseMatchStarted() => MatchStarted?.Invoke();
        public static void RaiseMatchFinished(bool won, bool lowStackWin) => MatchFinished?.Invoke(won, lowStackWin);
        public static void RaiseHit(int count = 1) => Hit?.Invoke(count);
        public static void RaiseCounter() => Counter?.Invoke();
        public static void RaiseCounterKO() => CounterKO?.Invoke();
        public static void RaiseChain(int length) => Chain?.Invoke(length);
        public static void RaiseStack(int count) => Stack?.Invoke(count);
        public static void RaiseSpecial() => Special?.Invoke();
        public static void RaiseCharacterUsed(string characterId) => CharacterUsed?.Invoke(characterId);

        public static void RaiseMatchLost() => MatchLost?.Invoke();
        public static void RaisePassiveTriggered(DarumaCharacter character) => PassiveTriggered?.Invoke(character);
        public static void RaisePassiveActivated() => PassiveActivated?.Invoke();
        public static void RaiseBlockTypesInStack(int count) => BlockTypesInStack?.Invoke(count);
        public static void RaiseRally(int count) => Rally?.Invoke(count);
        public static void RaiseOpportunistKO() => OpportunistKO?.Invoke();
        public static void RaiseChainKO() => ChainKO?.Invoke();
        public static void RaiseMultiKO(int count) => MultiKO?.Invoke(count);
        public static void RaiseNoDamageWin() => NoDamageWin?.Invoke();
        public static void RaiseHammerMiss() => HammerMiss?.Invoke();
        public static void RaiseQuickWin() => QuickWin?.Invoke();

        // ドメインの再読み込みをしない設定でも、再生のたびに受け取り手を空にする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            MatchStarted = null;
            MatchFinished = null;
            Hit = null;
            Counter = null;
            CounterKO = null;
            Chain = null;
            Stack = null;
            Special = null;
            CharacterUsed = null;
            MatchLost = null;
            PassiveTriggered = null;
            PassiveActivated = null;
            BlockTypesInStack = null;
            Rally = null;
            OpportunistKO = null;
            ChainKO = null;
            MultiKO = null;
            NoDamageWin = null;
            HammerMiss = null;
            QuickWin = null;
        }
    }
}
