using System.Collections.Generic;
using UnityEngine;

// ビデオ設定パネル。選択肢を切り替えるとすぐに GameSettings へ反映する。
// （画面モード・解像度はエディタ上では変化しない。ビルドで確認すること）
public class VideoSettingsPanel : MonoBehaviour
{
    [SerializeField] OptionSelector screenModeSelector;
    [SerializeField] OptionSelector resolutionSelector;
    [SerializeField] OptionSelector vSyncSelector;

    readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    void Start()
    {
        if (screenModeSelector != null)
        {
            screenModeSelector.SetOptions(new[] { "フルスクリーン", "ウィンドウ" }, GameSettings.FullScreen ? 0 : 1);
            screenModeSelector.onValueChanged.AddListener(i => GameSettings.SetFullScreen(i == 0));
        }

        if (resolutionSelector != null)
        {
            CollectResolutions();
            var labels = new string[resolutions.Count];
            int current = 0;
            for (int i = 0; i < resolutions.Count; i++)
            {
                labels[i] = resolutions[i].x + " × " + resolutions[i].y;
                if (resolutions[i].x == GameSettings.Width && resolutions[i].y == GameSettings.Height) current = i;
            }
            resolutionSelector.SetOptions(labels, current);
            resolutionSelector.onValueChanged.AddListener(i => GameSettings.SetResolution(resolutions[i].x, resolutions[i].y));
        }

        if (vSyncSelector != null)
        {
            vSyncSelector.SetOptions(new[] { "オン", "オフ" }, GameSettings.VSync ? 0 : 1);
            vSyncSelector.onValueChanged.AddListener(i => GameSettings.SetVSync(i == 0));
        }
    }

    // モニターが対応する解像度を、重複（リフレッシュレート違い）を除いて集める
    void CollectResolutions()
    {
        resolutions.Clear();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (size.x >= 1280 && !resolutions.Contains(size)) resolutions.Add(size);
        }

        // エディタなどで取得できなかったときの予備
        if (resolutions.Count == 0)
        {
            resolutions.Add(new Vector2Int(1280, 720));
            resolutions.Add(new Vector2Int(1600, 900));
            resolutions.Add(new Vector2Int(1920, 1080));
        }

        var saved = new Vector2Int(GameSettings.Width, GameSettings.Height);
        if (saved.x > 0 && !resolutions.Contains(saved)) resolutions.Add(saved);
        resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
    }

    void OnDisable() => GameSettings.Save();
}
