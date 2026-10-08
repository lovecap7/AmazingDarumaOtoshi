using TMPro;
using UnityEngine;
using UnityEngine.UI;

// サウンド設定パネル。スライダーを動かすとすぐに GameSettings へ反映する。
public class SoundSettingsPanel : MonoBehaviour
{
    [SerializeField] Slider masterSlider;
    [SerializeField] Slider bgmSlider;
    [SerializeField] Slider seSlider;

    [SerializeField] TMP_Text masterValue;
    [SerializeField] TMP_Text bgmValue;
    [SerializeField] TMP_Text seValue;

    bool bound;

    // コードから組み立てるときに部品を渡す
    public void Setup(Slider master, Slider bgm, Slider se, TMP_Text masterLabel, TMP_Text bgmLabel, TMP_Text seLabel)
    {
        masterSlider = master; bgmSlider = bgm; seSlider = se;
        masterValue = masterLabel; bgmValue = bgmLabel; seValue = seLabel;
        BindAll();
    }

    void Start()
    {
        if (!bound) BindAll();
    }

    void BindAll()
    {
        bound = true;
        Bind(masterSlider, masterValue, GameSettings.MasterVolume, GameSettings.SetMasterVolume);
        Bind(bgmSlider, bgmValue, GameSettings.BgmVolume, GameSettings.SetBgmVolume);
        Bind(seSlider, seValue, GameSettings.SeVolume, GameSettings.SetSeVolume);
    }

    // スライダーは 0〜10 の整数段階（コントローラーで1目盛りずつ動かしやすいように）
    static void Bind(Slider slider, TMP_Text label, float current, System.Action<float> setter)
    {
        if (slider == null) return;

        slider.minValue = 0f;
        slider.maxValue = 10f;
        slider.wholeNumbers = true;
        slider.SetValueWithoutNotify(Mathf.Round(current * 10f));
        UpdateLabel(label, slider.value);

        slider.onValueChanged.AddListener(v =>
        {
            setter(v / 10f);
            UpdateLabel(label, v);
        });
    }

    static void UpdateLabel(TMP_Text label, float v)
    {
        if (label != null) label.text = Mathf.RoundToInt(v * 10f).ToString();
    }

    void OnDisable() => GameSettings.Save();
}
