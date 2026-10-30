using System.Collections.Generic;
using UnityEngine;

// セレクト画面に並べるキャラクターとステージの一覧。ここに足せば画面に並ぶ（並びは自動で調整される）。
[CreateAssetMenu(menuName = "Daruma/Select Roster", fileName = "SelectRoster")]
public class SelectRoster : ScriptableObject
{
    public List<CharacterData> characters = new List<CharacterData>();
    public List<StageData> stages = new List<StageData>();

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
