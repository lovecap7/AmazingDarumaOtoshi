using System.Collections.Generic;
using UnityEngine;

// セレクト画面に並べるキャラクターとステージの一覧。ここに足せば画面に並ぶ（並びは自動で調整される）。
[CreateAssetMenu(menuName = "Daruma/Select Roster", fileName = "SelectRoster")]
public class SelectRoster : ScriptableObject
{
    public List<CharacterData> characters = new List<CharacterData>();

    [Tooltip("キャラクターセレクトとずかんに並べる順（左上から右へ。ドラッグで並べ替えられる）。空なら characters の順。" +
             "ここに無いキャラは並ばない（縦横のマスの数は SelectScene の MatchSelect で決める）")]
    public List<CharacterData> characterOrder = new List<CharacterData>();

    public List<StageData> stages = new List<StageData>();

    // 画面に並べるキャラ：characterOrder（空なら characters）の順に、空欄と重なりを除いたもの
    public List<CharacterData> OrderedCharacters()
    {
        var source = characterOrder != null && characterOrder.Count > 0 ? characterOrder : characters;
        var list = new List<CharacterData>();
        if (source != null) foreach (var c in source) if (c != null && !list.Contains(c)) list.Add(c);
        return list;
    }

    // ステージセレクト（ローカル）の動かせるプレビュー。メニュー「Fujihara > ステージ一覧を更新」で AllStages から自動で作る
    // （StageData は Nakagawa のフォルダにあるので、こちらに id で持つ）
    [System.Serializable]
    public class StagePreview
    {
        public string id;
        [Tooltip("見た目だけのステージ（Fujihara/Data/StagePreviews）")]
        public GameObject model;
        [Tooltip("カメラが見る点（ステージの中での位置）")]
        public Vector3 target;
        [Tooltip("カメラの距離と見下ろす角度（本編のカメラと同じ）")]
        public float distance = 30f, pitch = 55f;
    }
    public List<StagePreview> stagePreviews = new List<StagePreview>();

    public StagePreview PreviewOf(StageData stage)
    {
        if (stage == null) return null;
        return stagePreviews.Find(p => p.id == stage.id && p.model != null);
    }
}
