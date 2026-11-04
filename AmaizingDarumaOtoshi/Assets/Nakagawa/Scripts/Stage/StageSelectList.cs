using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // ステージセレクトに並べるステージ(StageInfo の Id)と、その順番
    //  ・メニュー「Fujihara > ステージ一覧を更新」(StageRosterSync)が読む。このアセットがあれば、ここに書いたステージだけを書いた順に並べる
    //    (ランダム(おまかせ)はセレクト画面が自動で最後に足すので、ここには書かない)
    //  ・Id が空 / 見つからないものは飛ばす。リストが空なら今まで通り AllStages の全ステージを並べる
    //  ・並べるステージを変えたら、このアセットを書き換えてから「ステージ一覧を更新」を実行する
    [CreateAssetMenu(menuName = "Nakagawa/Stage Select List", fileName = "StageSelectList")]
    public class StageSelectList : ScriptableObject
    {
        [Tooltip("ステージセレクトに並べるステージの Id(StageInfo.Id。例: Stage18, DebugStage21)。上から順に並ぶ")]
        public List<string> stageIds = new List<string>();

        [Tooltip("ステージセレクトで動かせるプレビュー(見た目だけのステージ)を書き出すフォルダ。空なら StageRosterSync の既定のフォルダ")]
        public string previewModelFolder = "Assets/Nakagawa/StagePreviewModels";

        // 並べる対象か(リストが空なら全部)
        public bool Contains(string id) => stageIds.Count == 0 || stageIds.Contains(id);

        // 並べる順番(リストにないものは後ろ)
        public int OrderOf(string id)
        {
            int i = stageIds.IndexOf(id);
            return i >= 0 ? i : int.MaxValue;
        }
    }
}
