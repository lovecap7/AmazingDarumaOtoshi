using UnityEngine;

// 試合（ゲーム画面）の音の一覧。Assets/Fujihara/Resources/MatchSounds.asset を Inspector で編集する。
// 空欄の音は、SoundSynth が作る仮の音で鳴る。本番の音ができたら、ここに AudioClip をドラッグして差し替える。
[CreateAssetMenu(menuName = "Daruma/Match Sounds", fileName = "MatchSounds")]
public class MatchSounds : ScriptableObject
{
    [Header("音量（メニューのサウンド設定にさらに掛ける）")]
    [Range(0f, 1f)] public float bgmVolume = 0.45f;
    [Range(0f, 1f)] public float seVolume = 0.9f;

    [Header("BGM")]
    public AudioClip bgm;
    [Tooltip("残り10秒になったら BGM を少し速く・高くする倍率")]
    public float hurryPitch = 1.06f;

    [Header("ハンマー・積み木")]
    public AudioClip swing;             // ハンマーを振る
    public AudioClip shotSwing;         // 股抜きショット
    public AudioClip hitBlock;          // ハンマーが積み木に当たる
    public AudioClip hitReturn;         // 飛んできた積み木を打ち返す
    public AudioClip hitCharacter;      // ハンマーがキャラクターに当たる
    public AudioClip blockHitCharacter; // 飛んできた積み木がキャラクターに当たる
    public AudioClip blockHitBlock;     // 積み木どうしがぶつかる
    public AudioClip pickup;            // 積み木を拾う
    public AudioClip jump;              // ジャンプ

    [Header("やられた・復活")]
    public AudioClip ko;                // 顔を飛ばされた
    public AudioClip fall;              // 場外に落ちた
    public AudioClip ghost;             // 幽霊になった
    public AudioClip respawn;           // 復活した
    public AudioClip playerOut;         // 脱落した（ストック）
    public AudioClip scoreUp;           // 点が増えた（タイム）
    public AudioClip scoreDown;         // 点が減った（タイム）

    [Header("試合の進行")]
    public AudioClip startWhistle;      // 試合開始
    public AudioClip tick;              // 残り10〜4秒
    public AudioClip tickLast;          // 残り3〜1秒
    public AudioClip finishWhistle;     // 試合終了（最後の1人）
    public AudioClip timeUpWhistle;     // タイムアップ
    public AudioClip victory;           // 勝者が決まった
    public AudioClip draw;              // 引き分け

    // ---- 鳴らす側が使う（空欄なら仮の音） ----
    public AudioClip Bgm => bgm != null ? bgm : SoundSynth.Bgm;
    public AudioClip Swing => swing != null ? swing : SoundSynth.Swing;
    public AudioClip ShotSwing => shotSwing != null ? shotSwing : SoundSynth.ShotSwing;
    public AudioClip HitBlock => hitBlock != null ? hitBlock : SoundSynth.HitBlock;
    public AudioClip HitReturn => hitReturn != null ? hitReturn : SoundSynth.HitReturn;
    public AudioClip HitCharacter => hitCharacter != null ? hitCharacter : SoundSynth.HitCharacter;
    public AudioClip BlockHitCharacter => blockHitCharacter != null ? blockHitCharacter : SoundSynth.BlockHitCharacter;
    public AudioClip BlockHitBlock => blockHitBlock != null ? blockHitBlock : SoundSynth.BlockHitBlock;
    public AudioClip Pickup => pickup != null ? pickup : SoundSynth.Pickup;
    public AudioClip Jump => jump != null ? jump : SoundSynth.Jump;
    public AudioClip KO => ko != null ? ko : SoundSynth.KO;
    public AudioClip Fall => fall != null ? fall : SoundSynth.Fall;
    public AudioClip Ghost => ghost != null ? ghost : SoundSynth.Ghost;
    public AudioClip Respawn => respawn != null ? respawn : SoundSynth.Respawn;
    public AudioClip PlayerOut => playerOut != null ? playerOut : SoundSynth.Out;
    public AudioClip ScoreUp => scoreUp != null ? scoreUp : SoundSynth.ScoreUp;
    public AudioClip ScoreDown => scoreDown != null ? scoreDown : SoundSynth.ScoreDown;
    public AudioClip StartWhistle => startWhistle != null ? startWhistle : SoundSynth.StartWhistle;
    public AudioClip Tick => tick != null ? tick : SoundSynth.Tick;
    public AudioClip TickLast => tickLast != null ? tickLast : SoundSynth.TickLast;
    public AudioClip FinishWhistle => finishWhistle != null ? finishWhistle : SoundSynth.FinishWhistle;
    public AudioClip TimeUpWhistle => timeUpWhistle != null ? timeUpWhistle : SoundSynth.TimeUpWhistle;
    public AudioClip Victory => victory != null ? victory : SoundSynth.Victory;
    public AudioClip Draw => draw != null ? draw : SoundSynth.Draw;

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
