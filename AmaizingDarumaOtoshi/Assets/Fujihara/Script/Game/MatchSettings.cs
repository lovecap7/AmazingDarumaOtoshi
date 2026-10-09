using Nakahira;
using UnityEngine;

// 試合（MatchDirector）で使う部品と数値。Assets/Fujihara/Resources/MatchSettings.asset を Inspector で編集する。
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

    [Header("復活")]
    [Tooltip("ストック・タイムで、やられてから出てくるまでの時間（秒）")]
    public float respawnDelay = 1.5f;
    [Tooltip("復活したあとの無敵時間（秒）。ゴーストの幽霊からの復活も同じ")]
    public float respawnInvincibleTime = 2f;

    [Header("試合の終わり")]
    [Tooltip("試合が終わってから、結果を見せてセレクトへ戻るまでの時間（秒）")]
    public float resultTime = 4f;

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
