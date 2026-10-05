using System;
using UnityEngine;

// サウンド・ビデオ設定の保存と反映を担当する。
// 値は PlayerPrefs に保存され、ゲーム起動時に自動で反映される。
// BGM・効果音を鳴らす側は BgmVolume / SeVolume を AudioSource.volume に掛けて使う。
public static class GameSettings
{
    const string KeyMaster = "Settings.MasterVolume";
    const string KeyBgm = "Settings.BgmVolume";
    const string KeySe = "Settings.SeVolume";
    const string KeyFullScreen = "Settings.FullScreen";
    const string KeyWidth = "Settings.Width";
    const string KeyHeight = "Settings.Height";
    const string KeyVSync = "Settings.VSync";

    public static float MasterVolume { get; private set; } = 1f;
    public static float BgmVolume { get; private set; } = 0.8f;
    public static float SeVolume { get; private set; } = 0.8f;

    public static bool FullScreen { get; private set; } = true;
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static bool VSync { get; private set; } = true;

    // 音量が変わったときに呼ばれる（鳴っている BGM の音量を追従させる用）
    public static event Action AudioChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        AudioChanged = null;
        Load();
        ApplyAudio();
        ApplyVideo();
    }

    static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat(KeyMaster, 1f);
        BgmVolume = PlayerPrefs.GetFloat(KeyBgm, 0.8f);
        SeVolume = PlayerPrefs.GetFloat(KeySe, 0.8f);

        FullScreen = PlayerPrefs.GetInt(KeyFullScreen, 1) == 1;
        Width = PlayerPrefs.GetInt(KeyWidth, Screen.currentResolution.width);
        Height = PlayerPrefs.GetInt(KeyHeight, Screen.currentResolution.height);
        VSync = PlayerPrefs.GetInt(KeyVSync, 1) == 1;
    }

    // ---------------- サウンド ----------------
    public static void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyMaster, MasterVolume); ApplyAudio(); }
    public static void SetBgmVolume(float v) { BgmVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyBgm, BgmVolume); ApplyAudio(); }
    public static void SetSeVolume(float v) { SeVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeySe, SeVolume); ApplyAudio(); }

    static void ApplyAudio()
    {
        AudioListener.volume = MasterVolume;
        AudioChanged?.Invoke();
    }

    // ---------------- ビデオ ----------------
    public static void SetFullScreen(bool on) { FullScreen = on; PlayerPrefs.SetInt(KeyFullScreen, on ? 1 : 0); ApplyVideo(); }
    public static void SetResolution(int w, int h) { Width = w; Height = h; PlayerPrefs.SetInt(KeyWidth, w); PlayerPrefs.SetInt(KeyHeight, h); ApplyVideo(); }
    public static void SetVSync(bool on) { VSync = on; PlayerPrefs.SetInt(KeyVSync, on ? 1 : 0); ApplyVideo(); }
    // エディタではビデオ設定を反映しない（QualitySettings を触るとプロジェクト設定が書き換わってしまうため）。
    // 効果はビルドで確認すること
    static void ApplyVideo()
    {
#if !UNITY_EDITOR
        QualitySettings.vSyncCount = VSync ? 1 : 0;

        var mode = FullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(Width, Height, mode);
#endif
    }

    // 設定画面を閉じたときなどに呼んでディスクへ書き込む
    public static void Save() => PlayerPrefs.Save();
}
