using UnityEngine;

// 試合（ゲーム画面）の音。Assets/Fujihara/Resources/MatchSounds.asset を Inspector で編集する。
// Inspector で設定できるのは次の4つだけ（AudioClip をドラッグして入れる。空欄なら SoundSynth が作った仮の音）
//   1. 振った音　　　　：ハンマーを振ったとき（薙ぎ払い・股抜きショット・幽霊の薙ぎ払い）
//   2. 積み木に当たった音：振ったハンマーが積み木に当たったとき（打ち返しも。打ち返すほど高くなる）
//   3. 積み木を飛ばした音：飛ばした積み木が相手に当たったとき／ハンマーで相手の積み木を直接はじき出したとき
//   4. 撃墜の音　　　　　：首を飛ばして撃墜したとき
// それ以外の音（BGM・笛・拾う・ジャンプ など）は、今は仮の音のまま（設定項目は出さない）。
[CreateAssetMenu(menuName = "Daruma/Match Sounds", fileName = "MatchSounds")]
public class MatchSounds : ScriptableObject
{
    [Tooltip("ハンマーを振ったとき（空欄なら仮の音）")]
    public AudioClip swing;
    [Tooltip("振ったハンマーが積み木に当たったとき。打ち返しも（空欄なら仮の音）")]
    public AudioClip hitBlock;
    [Tooltip("飛ばした積み木が相手に当たったとき／ハンマーで相手の積み木を直接はじき出したとき（空欄なら仮の音）")]
    public AudioClip knock;
    [Tooltip("首を飛ばして撃墜したとき（空欄なら仮の音）")]
    public AudioClip ko;

    [Tooltip("打ち返したときの音。打ち返し1回目は①、それを打ち返すと②、③…と上がり、段数を超えたらずっと最後の音。空欄なら「積み木に当たった音」を高くして鳴らす")]
    public AudioClip[] returnHits = new AudioClip[4];
    [Tooltip("最後の段を超えた打ち返しで、1回ごとに上げる音の高さ（半音）。0 なら上げない")]
    [Range(0f, 3f)] public float returnPitchStep = 1f;
    [Tooltip("最後の段から、どこまで高くするか（半音。12 で1オクターブ上）")]
    [Range(0f, 24f)] public float returnPitchMax = 12f;

    [Tooltip("聞き比べ用の候補の音。Inspector の「候補の音」で ▶ 試聴し、1〜4 のボタンでその場面に入れる（試合では鳴らない）")]
    public System.Collections.Generic.List<AudioClip> candidates = new System.Collections.Generic.List<AudioClip>();

    // ---- 設定できる4つ ----
    public AudioClip Swing => swing != null ? swing : SoundSynth.Swing;
    public AudioClip HitBlock => hitBlock != null ? hitBlock : SoundSynth.HitBlock;
    public AudioClip Knock => knock != null ? knock : SoundSynth.HitCharacter;
    public AudioClip KO => ko != null ? ko : SoundSynth.KO;

    // 打ち返した回数（1〜）の音。①〜④を順に、5回目からはずっと④（入っていなければ null）
    public AudioClip ReturnHit(int count)
    {
        if (returnHits == null || returnHits.Length == 0 || count <= 0) return null;
        return returnHits[Mathf.Min(count, returnHits.Length) - 1];
    }

    // 打ち返した回数（1〜）の音の高さ（倍率）。最後の段までは 1、そこから1回ごとに returnPitchStep 半音ずつ上げる（returnPitchMax まで）
    public float ReturnPitch(int count)
    {
        int over = count - (returnHits != null ? returnHits.Length : 0);
        if (over <= 0) return 1f;
        float semitones = Mathf.Min(over * returnPitchStep, returnPitchMax);
        return Mathf.Pow(2f, semitones / 12f);
    }

    // ---- 設定しない音（仮の音のまま） ----
    public float BgmVolume => 0.45f;
    public float SeVolume => 0.9f;
    public float HurryPitch => 1.06f;   // 残り10秒で BGM を少し速く・高くする倍率
    public AudioClip Bgm => MenuSounds.Instance != null && MenuSounds.Instance.matchBgm != null ? MenuSounds.Instance.matchBgm : SoundSynth.Bgm;
    public AudioClip BlockHitBlock => SoundSynth.BlockHitBlock;
    public AudioClip Pickup => SoundSynth.Pickup;
    public AudioClip Jump => SoundSynth.Jump;
    public AudioClip Fall => SoundSynth.Fall;
    public AudioClip Ghost => SoundSynth.Ghost;
    public AudioClip Respawn => SoundSynth.Respawn;
    public AudioClip PlayerOut => SoundSynth.Out;
    public AudioClip ScoreUp => SoundSynth.ScoreUp;
    public AudioClip ScoreDown => SoundSynth.ScoreDown;
    public AudioClip StartWhistle => SoundSynth.StartWhistle;
    public AudioClip Tick => SoundSynth.Tick;
    public AudioClip TickLast => SoundSynth.TickLast;
    public AudioClip FinishWhistle => SoundSynth.FinishWhistle;
    public AudioClip TimeUpWhistle => SoundSynth.TimeUpWhistle;
    public AudioClip Victory => SoundSynth.Victory;
    public AudioClip Draw => SoundSynth.Draw;

    static MatchSounds s_instance;
    public static MatchSounds Instance
    {
        get
        {
            if (s_instance == null) s_instance = Resources.Load<MatchSounds>("MatchSounds");
            if (s_instance == null) s_instance = CreateInstance<MatchSounds>();   // アセットが無くても仮の音で鳴らす
            return s_instance;
        }
    }
}
