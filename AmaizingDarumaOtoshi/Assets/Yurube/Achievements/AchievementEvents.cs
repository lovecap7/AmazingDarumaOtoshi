using System;
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

        public static void RaiseMatchStarted() => MatchStarted?.Invoke();
        public static void RaiseMatchFinished(bool won, bool lowStackWin) => MatchFinished?.Invoke(won, lowStackWin);
        public static void RaiseHit(int count = 1) => Hit?.Invoke(count);
        public static void RaiseCounter() => Counter?.Invoke();
        public static void RaiseCounterKO() => CounterKO?.Invoke();
        public static void RaiseChain(int length) => Chain?.Invoke(length);
        public static void RaiseStack(int count) => Stack?.Invoke(count);
        public static void RaiseSpecial() => Special?.Invoke();
        public static void RaiseCharacterUsed(string characterId) => CharacterUsed?.Invoke(characterId);

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
        }
    }
}
