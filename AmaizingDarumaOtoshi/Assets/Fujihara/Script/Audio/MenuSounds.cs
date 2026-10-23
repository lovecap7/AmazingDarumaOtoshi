using UnityEngine;

// メニュー操作の音。Assets/Fujihara/Resources/MenuSounds.asset を Inspector で編集する。
//   1. カーソル移動：選択を上下左右に動かしたとき
//   2. 決定　　　　：A で決めたとき
//   3. キャンセル　：B で戻ったとき
// 鳴らすときは MenuSounds.Cursor() / Decide() / Cancel()（シーンが切り替わっても途中で切れない）
[CreateAssetMenu(menuName = "Daruma/Menu Sounds", fileName = "MenuSounds")]
public class MenuSounds : ScriptableObject
{
    const float Volume = 0.9f;
    [Tooltip("選択を動かしたとき")]
    public AudioClip cursor;
    [Tooltip("決定したとき")]
    public AudioClip decide;
    [Tooltip("キャンセル・戻ったとき")]
    public AudioClip cancel;

    [Tooltip("聞き比べ用の候補の音。Inspector の「候補の音」で ▶ 試聴し、1〜3 のボタンでその場面に入れる")]
    public System.Collections.Generic.List<AudioClip> candidates = new System.Collections.Generic.List<AudioClip>();

    // ---- BGM（まだ各画面では鳴らしていない。曲を決めてからつなぐ） ----
    [Tooltip("タイトル画面の BGM")]
    public AudioClip titleBgm;
    [Tooltip("メニュー画面の BGM")]
    public AudioClip menuBgm;
    [Tooltip("セレクト画面（モード・ルール・キャラ・ステージ）の BGM")]
    public AudioClip selectBgm;

    [Tooltip("聞き比べ用の BGM 候補。Inspector の「BGM の候補」で ▶ 試聴し、T / M / S のボタンでその画面に入れる")]
    public System.Collections.Generic.List<AudioClip> bgmCandidates = new System.Collections.Generic.List<AudioClip>();

    // ---- 鳴らす ----
    public static void Cursor() => Play(Instance.cursor);
    public static void Decide() => Play(Instance.decide);
    public static void Cancel() => Play(Instance.cancel);

    static AudioSource s_source;
    static int s_lastFrame = -1;
    static AudioClip s_lastClip;

    public static void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        // 同じフレームに同じ音を重ねない（複数のプレイヤーが同時に押したときなど）
        if (s_lastFrame == Time.frameCount && s_lastClip == clip) return;
        s_lastFrame = Time.frameCount; s_lastClip = clip;
        if (s_source == null)
        {
            var go = new GameObject("MenuSounds");
            DontDestroyOnLoad(go);
            s_source = go.AddComponent<AudioSource>();
            s_source.playOnAwake = false;
            s_source.spatialBlend = 0f;
            s_source.ignoreListenerPause = true;
        }
        s_source.PlayOneShot(clip, volume * Volume * GameSettings.SeVolume);
    }

    static MenuSounds s_instance;
    public static MenuSounds Instance
    {
        get
        {
            if (s_instance == null) s_instance = Resources.Load<MenuSounds>("MenuSounds");
            if (s_instance == null) s_instance = CreateInstance<MenuSounds>();
            return s_instance;
        }
    }
}
