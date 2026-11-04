using Nakahira;
using UnityEngine;

// 試合（セレクトから始めた DarumaMatch）で使う部品と数値。Assets/Fujihara/Resources/MatchSettings.asset を Inspector で編集する。
// ステージ側（出現ポイント・ステージの範囲）は各ステージのシーンから読むので、ここには置かない。
[CreateAssetMenu(menuName = "Daruma/Match Settings", fileName = "MatchSettings")]
public class MatchSettings : ScriptableObject
{
    [Header("部品")]
    [Tooltip("キャラクター（CharacterData に prefab が設定されていればそちらを使う）")]
    public DarumaCharacter characterPrefab;
    [Tooltip("ゴーストモードで、やられたプレイヤーがなる幽霊")]
    public DarumaGhost ghostPrefab;
    [Tooltip("ゲームパッドで操作する人の入力（PlayerInput + DarumaInputHandler）")]
    public GameObject gamepadInputPrefab;

    [Header("CPU")]
    [Tooltip("CPU（DarumaCpu）の考え方・腕前。強さ 1（よわい）・2（ふつう）・3（つよい）の順。足りない強さは一番近いものを使う。空なら仮の CPU（CpuDarumaInput）")]
    public CpuParams[] cpuParams;

    // 強さ（1〜）に合う CpuParams。無ければ null
    public CpuParams CpuParamsFor(int level)
    {
        if (cpuParams == null) return null;
        for (int i = Mathf.Clamp(level, 1, cpuParams.Length) - 1; i >= 0; i--)
        {
            if (cpuParams[i] != null) return cpuParams[i];
        }
        foreach (var p in cpuParams) if (p != null) return p;
        return null;
    }

    [Header("復活")]
    [Tooltip("ストック・タイムで、やられてから出てくるまでの時間（秒）")]
    public float respawnDelay = 1.5f;
    [Tooltip("復活したあとの無敵時間（秒）。ゴーストの幽霊からの復活も同じ")]
    public float respawnInvincibleTime = 2f;

    [Header("試合の終わり")]
    [Tooltip("試合が終わってから、結果を見せてセレクトへ戻るまでの時間（秒）")]
    public float resultTime = 4f;

    [Header("リザルト（表彰）")]
    [Tooltip("試合が終わったら、セレクトへ戻る前にリザルト画面（1位の表彰）を見せる")]
    public bool useResultScene = true;
    [Tooltip("リザルト画面のシーン名")]
    public string resultSceneName = "ResultScene";
    [Tooltip("試合が終わってから、リザルト画面へ移るまでの時間（秒）。「しあい しゅうりょう！」を見せる間")]
    public float resultSceneDelay = 2f;
    [Tooltip("ストックのリザルト画面（空欄なら上の「リザルト画面のシーン名」）")]
    public string stockResultSceneName = "ResultTowerScene";
    [Tooltip("ゴーストのリザルト画面（空欄なら上の「リザルト画面のシーン名」）")]
    public string ghostResultSceneName = "ResultTowerScene";
    [Tooltip("タイムのリザルト画面（空欄なら上の「リザルト画面のシーン名」）")]
    public string timeResultSceneName = "ResultTowerScene";

    // モードに合わせたリザルト画面のシーン名
    public string ResultSceneFor(MatchSetup.MatchMode mode)
    {
        string scene = mode == MatchSetup.MatchMode.Stock ? stockResultSceneName
                     : mode == MatchSetup.MatchMode.Ghost ? ghostResultSceneName
                     : timeResultSceneName;
        return string.IsNullOrEmpty(scene) ? resultSceneName : scene;
    }

    static MatchSettings s_instance;
    public static MatchSettings Instance
    {
        get
        {
            if (s_instance == null) s_instance = Resources.Load<MatchSettings>("MatchSettings");
            return s_instance;
        }
    }
}
