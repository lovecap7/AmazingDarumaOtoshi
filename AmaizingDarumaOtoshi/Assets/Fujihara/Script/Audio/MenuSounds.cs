using UnityEngine;

// メニュー操作の音。Assets/Fujihara/Resources/MenuSounds.asset を Inspector で編集する。
//   1. カーソル移動：選択を上下左右に動かしたとき
//   2. 決定　　　　：A で決めたとき
//   3. キャンセル　：B で戻ったとき
// ※まだ各画面からは鳴らしていない（音を決めてからつなぐ）
[CreateAssetMenu(menuName = "Daruma/Menu Sounds", fileName = "MenuSounds")]
public class MenuSounds : ScriptableObject
{
    [Tooltip("選択を動かしたとき")]
    public AudioClip cursor;
    [Tooltip("決定したとき")]
    public AudioClip decide;
    [Tooltip("キャンセル・戻ったとき")]
    public AudioClip cancel;

    [Tooltip("聞き比べ用の候補の音。Inspector の「候補の音」で ▶ 試聴し、1〜3 のボタンでその場面に入れる")]
    public System.Collections.Generic.List<AudioClip> candidates = new System.Collections.Generic.List<AudioClip>();

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
