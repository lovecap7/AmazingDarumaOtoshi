using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // 時間を止める・遅くする演出(必殺技のカットイン・いわだるの着弾のヒットストップ)が使う共通の仕組み。
    // 前は演出ごとに「始まったときの timeScale を覚えて、終わりに戻す」作りだったので、
    // 演出が重なると止まった値(0)を覚えてしまい、時間が止まったままになっていた。
    // ここでは止めている演出を数え、いちばん遅い値を使う。最後の演出が終わったときだけ元の速さへ戻す。
    public static class TimeStop
    {
        // 止めている演出と、その演出が望む時間の速さ
        static readonly Dictionary<object, float> s_owners = new Dictionary<object, float>();
        // 最初の演出が始まったときの速さ(試合の外で戻す値)
        static float s_baseScale = 1.0f;

        // 何かの演出が時間を止めている・遅くしているか
        public static bool IsActive => s_owners.Count > 0;

        // 再生のたびに空にする(ドメインの再読み込みをしない設定でも前の再生の値が残らないように)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_owners.Clear();
            s_baseScale = 1.0f;
        }

        // owner の演出が時間を scale にする(同じ owner で呼び直すと値だけ変わる)
        public static void Begin(object owner, float scale)
        {
            if (owner == null) return;
            if (s_owners.Count == 0) s_baseScale = Time.timeScale;
            s_owners[owner] = Mathf.Max(0.0f, scale);
            Apply();
        }

        // owner の演出が終わった。ほかに止めている演出がなければ元の速さへ戻す
        public static void End(object owner)
        {
            if (owner == null || !s_owners.Remove(owner)) return;
            if (s_owners.Count > 0)
            {
                Apply();
                return;
            }
            Time.timeScale = RestoreScale();
        }

        // 止めている演出のうち、いちばん遅い値にする
        static void Apply()
        {
            float scale = float.MaxValue;
            foreach (var value in s_owners.Values) scale = Mathf.Min(scale, value);
            Time.timeScale = scale;
        }

        // 戻す速さ。試合中は試合の速さ(やられ演出のスロー中に始まっても、スローのまま残らないように)。
        // ポーズ中・試合が終わって止めているときは 0 のまま(ポーズを解く・セレクトへ戻るときに試合側が戻す)
        static float RestoreScale()
        {
            var match = DarumaMatch.Current;
            if (match == null) return s_baseScale;
            if (match.IsPaused || match.IsFinished) return 0.0f;
            return DarumaMatch.PlaySpeed;
        }
    }
}
