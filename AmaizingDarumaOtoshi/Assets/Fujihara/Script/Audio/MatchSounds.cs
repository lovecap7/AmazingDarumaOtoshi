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

    [Tooltip("聞き比べ用の候補の音。Inspector の「候補の音」で ▶ 試聴し、1〜4 のボタンでその場面に入れる（試合では鳴らない）")]
    public System.Collections.Generic.List<AudioClip> candidates = new System.Collections.Generic.List<AudioClip>();

    // ---- 設定できる4つ ----
    public AudioClip Swing => swing != null ? swing : SoundSynth.Swing;
    public AudioClip HitBlock => hitBlock != null ? hitBlock : SoundSynth.HitBlock;
    public AudioClip Knock => knock != null ? knock : SoundSynth.HitCharacter;
    public AudioClip KO => ko != null ? ko : SoundSynth.KO;

    // ---- 設定しない音（仮の音のまま） ----
    public float BgmVolume => 0.45f;
    public float SeVolume => 0.9f;
    public float HurryPitch => 1.06f;   // 残り10秒で BGM を少し速く・高くする倍率
    public AudioClip Bgm => SoundSynth.Bgm;
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
