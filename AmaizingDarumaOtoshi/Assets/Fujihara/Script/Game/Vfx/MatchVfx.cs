using Nakahira;
using UnityEngine;

// 試合の演出をまとめて動かす係。ゲームが始まると自動で1つ作られ、シーンをまたいで残る（シーン・プレハブには置かない）。
// 演出ごとの中身は DustVfx（土埃）・SwingTrailVfx（ハンマーの軌跡）・RallyVfx（打ち返しの衝撃波）・KillExplosionVfx（即死の大爆発）・
// ExcaliburVfx（だるきんぐの必殺技）。
// どれも試合を読むだけで、試合の動き（当たり判定・時間）には影響しない。設定は MatchVfxSettings
public class MatchVfx : MonoBehaviour
{
    static MatchVfx s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (s_instance != null) return;
        var go = new GameObject("MatchVfx");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<MatchVfx>();
        go.AddComponent<DustVfx>();
        go.AddComponent<SwingTrailVfx>();
        go.AddComponent<RallyVfx>();
        go.AddComponent<KillExplosionVfx>();
        go.AddComponent<ExcaliburVfx>();
    }

    // 体（生きている体・幽霊）から、そのプレイヤーを探す（試合が無い・見つからなければ null）
    public static MatchPlayer PlayerOf(Component who)
    {
        var match = DarumaMatch.Current;
        if (match == null || who == null) return null;
        foreach (var p in match.Players)
            if (p != null && ((p.Character != null && p.Character == who) || (p.Ghost != null && p.Ghost == who))) return p;
        return null;
    }

    // そのプレイヤーの演出の色（CPU の灰色は、少し明るくして見えやすく）
    public static Color ColorOf(MatchPlayer p)
    {
        if (p == null) return Color.white;
        var c = p.Color;
        float s = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        return s < 0.1f ? Color.Lerp(c, Color.white, 0.5f) : c;
    }
}
