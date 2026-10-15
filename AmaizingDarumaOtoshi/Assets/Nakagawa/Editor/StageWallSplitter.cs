using System.Collections.Generic;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa.EditorTools
{
    // ステージの壁(ReflectWall)を、床から kVisibleHeight までの見える壁と、その上の見えない壁(InvWall)に分ける
    // 見えない壁は当たり判定・反射だけを持つ(描画しない)。高い壁が床やプレイヤーを隠さず、それでも乗り越えられない
    //  ・既に分けてある壁(高さが kVisibleHeight 以下)はそのまま。何度実行してもよい
    //  ・StageCombiner がステージをまとめるとき(壁の高さを伸ばした後)にも使う
    public static class StageWallSplitter
    {
        public const string kInvisibleName = "InvWall";
        // 見える壁の高さ(床から)
        public const float kVisibleHeight = 1.0f;
        const float kEps = 0.01f;

        [MenuItem("Nakagawa/Stages/Split Walls Into Visible And Invisible (Open Scenes)")]
        public static void SplitOpenScenes()
        {
            int total = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                int count = 0;
                foreach (var root in scene.GetRootGameObjects()) count += Split(root.transform);
                if (count > 0) EditorSceneManager.MarkSceneDirty(scene);
                total += count;
            }
            Debug.Log($"[StageWallSplitter] 壁 {total} 枚を、高さ {kVisibleHeight} の見える壁と見えない壁({kInvisibleName})に分けました。シーンを保存してください");
        }

        // root の下の壁をすべて分ける。分けた枚数を返す
        public static int Split(Transform root)
        {
            int count = 0;
            var walls = new List<ReflectWall>(root.GetComponentsInChildren<ReflectWall>(true));
            foreach (var wall in walls)
            {
                if (SplitWall(wall.transform)) count++;
            }
            return count;
        }

        // root の下の見えない壁を取り除く(壁を作り直す前に使う)
        public static void RemoveInvisible(Transform root)
        {
            foreach (var wall in new List<ReflectWall>(root.GetComponentsInChildren<ReflectWall>(true)))
            {
                if (wall.name == kInvisibleName) Undo.DestroyObjectImmediate(wall.gameObject);
            }
        }

        // 回転していない箱の壁1枚を分ける。分けたら true
        static bool SplitWall(Transform wall)
        {
            if (wall.name == kInvisibleName) return false;
            if (Vector3.Dot(wall.up, Vector3.up) < 0.999f) return false;

            Vector3 scale = wall.localScale;
            Vector3 pos = wall.localPosition;
            float height = scale.y;
            if (height <= kVisibleHeight + kEps) return false;
            float bottom = pos.y - height * 0.5f;

            // 上の部分: 壁を複製して(当たり判定・反射の目印ごと)見えなくする
            var upper = Object.Instantiate(wall.gameObject, wall.parent);
            Undo.RegisterCreatedObjectUndo(upper, "Split Wall");
            upper.name = kInvisibleName;
            upper.transform.SetSiblingIndex(wall.GetSiblingIndex() + 1);
            float upperHeight = height - kVisibleHeight;
            upper.transform.localScale = new Vector3(scale.x, upperHeight, scale.z);
            upper.transform.localPosition = new Vector3(pos.x, bottom + kVisibleHeight + upperHeight * 0.5f, pos.z);
            foreach (var r in upper.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // 下の部分: 元の壁を見える高さまで縮める
            Undo.RecordObject(wall, "Split Wall");
            wall.localScale = new Vector3(scale.x, kVisibleHeight, scale.z);
            wall.localPosition = new Vector3(pos.x, bottom + kVisibleHeight * 0.5f, pos.z);
            return true;
        }
    }
}
