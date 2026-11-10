using UnityEngine;

namespace Yurube.Achievements
{
    // シークレット実績のヒント。
    //   ・しきい値に達するまでは、条件もヒントも完全に伏せる
    //   ・関連する累計が半分（千本ノックなら命中500）に達するか、
    //     試合を一定回数プレイすると、ヒントを1つだけ出す
    // しきい値は AchievementDefinition の hintAtProgress / hintAtMatches で実績ごとに変えられる
    // （0 なら目標の半分・既定の試合数）。ここでは出すかどうかと文を選ぶだけ。
    public static class AchievementHints
    {
        // 試合数のしきい値を指定しなかったときの既定値
        public const int DefaultMatchThreshold = 30;
        // 進捗のしきい値を指定しなかったときに使う割合（目標の半分）
        public const float HalfRatio = 0.5f;

        // ヒントを出し始める進捗の数
        public static int ProgressThreshold(AchievementData data)
        {
            if (data == null) return int.MaxValue;
            if (data.HintAtProgress > 0) return data.HintAtProgress;
            int target = Mathf.Max(1, data.Target);
            return Mathf.Max(1, Mathf.CeilToInt(target * HalfRatio));
        }

        // ヒントを出し始める試合数
        public static int MatchThreshold(AchievementData data)
        {
            if (data == null) return DefaultMatchThreshold;
            return data.HintAtMatches > 0 ? data.HintAtMatches : DefaultMatchThreshold;
        }

        // ヒントを出してよい段階か（解除済みなら出す必要はない）
        public static bool IsRevealed(AchievementData data)
        {
            if (data == null) return false;
            if (data.IsUnlocked) return false;
            if (!data.HasHint) return false;

#if UNITY_EDITOR
            // エディターのデバッグ用（Yurube メニューの「実績ヒントの確認」で切り替える）
            if (DebugForceReveal) return true;
#endif

            // 関連する累計（上限で止めていない生の数）
            int progress = AchievementManager.CountOf(data.Definition.metric);
            if (progress >= ProgressThreshold(data)) return true;

            // 試合を一定回数プレイしていれば、そちらでも出す
            int matches = AchievementManager.CountOf(AchievementMetric.MatchPlayed);
            return matches >= MatchThreshold(data);
        }

        // いま出すヒントの文（伏せている間と解除後は空）
        public static string TextOf(AchievementData data) => IsRevealed(data) ? (data.Hint ?? "") : "";

#if UNITY_EDITOR
        // エディターのデバッグ用：しきい値を無視して、未解除のシークレット実績のヒントを必ず出す。
        // 値は EditorPrefs に残るので、再生し直しても（スクリプトを読み込み直しても）状態が続く。
        // 製品版には入らない。
        public static bool DebugForceReveal
        {
            get { return UnityEditor.EditorPrefs.GetBool(ForceRevealKey, false); }
            set { UnityEditor.EditorPrefs.SetBool(ForceRevealKey, value); }
        }

        const string ForceRevealKey = "Achievements.HintDebug.ForceReveal";
#endif
    }
}
