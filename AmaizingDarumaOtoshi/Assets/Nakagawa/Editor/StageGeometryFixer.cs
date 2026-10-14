using System.Collections.Generic;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa.EditorTools
{
    // ステージの床・壁(回転していない箱)どうしが重なって、同じ平面に同じ向きの面ができている所(ちらつきの原因)を直す
    //  ・壁: 角で重なっている壁は、長い向きの端を重なりの手前まで縮める(南北の壁はそのまま、東西の壁を縮めることが多い)
    //  ・床: 上面の高さが同じで重なっている床は、後の床から重なった部分を切り抜き、残りを箱に分ける(十字の交差点など)
    // 見た目・当たり判定の範囲(和集合)は変わらない。StageBuilder がステージを作るときにも使う
    public static class StageGeometryFixer
    {
        const float kEps = 0.005f;

        [MenuItem("Nakagawa/Stages/Fix Overlapping Floors And Walls (Open Scenes)")]
        public static void FixOpenScenes()
        {
            int total = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                int fixedCount = 0;
                var entries = new List<StageEntry>();
                foreach (var root in scene.GetRootGameObjects()) entries.AddRange(root.GetComponentsInChildren<StageEntry>(true));
                if (entries.Count > 0)
                {
                    foreach (var entry in entries) fixedCount += Fix(entry.transform);
                }
                else
                {
                    // ステージ1つ分のシーン: 床・壁は「Stage」の下
                    foreach (var root in scene.GetRootGameObjects()) fixedCount += Fix(root.transform);
                }
                if (fixedCount > 0) EditorSceneManager.MarkSceneDirty(scene);
                total += fixedCount;
            }
            Debug.Log($"[StageGeometryFixer] 重なっていた床・壁を {total} か所直しました。シーンを保存してください");
        }

        // stageRoot の下の床・壁を直す。直した数を返す
        public static int Fix(Transform stageRoot)
        {
            return FixWalls(stageRoot) + FixFloors(stageRoot);
        }

        //========================================
        // 壁
        //========================================

        static int FixWalls(Transform stageRoot)
        {
            var walls = new List<Transform>();
            foreach (var w in stageRoot.GetComponentsInChildren<ReflectWall>(true))
            {
                if (IsAxisAlignedBox(w.transform)) walls.Add(w.transform);
            }

            int count = 0;
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = 0; j < walls.Count; j++)
                {
                    if (i == j) continue;
                    if (TrimEnd(walls[j], WorldBox(walls[i]))) count++;
                }
            }
            return count;
        }

        // b の長い向きの端が a と重なっていて、重なりが b の幅いっぱいなら、b をその手前まで縮める
        static bool TrimEnd(Transform b, Bounds a)
        {
            Bounds bb = WorldBox(b);
            if (!OverlapXZ(a, bb, out Rect o) || !OverlapY(a, bb)) return false;

            bool alongZ = bb.size.z >= bb.size.x;
            float min = alongZ ? bb.min.z : bb.min.x;
            float max = alongZ ? bb.max.z : bb.max.x;
            float width = alongZ ? bb.size.x : bb.size.z;
            float oWidth = alongZ ? o.width : o.height;
            float oMin = alongZ ? o.yMin : o.xMin;
            float oMax = alongZ ? o.yMax : o.xMax;
            if (Mathf.Abs(oWidth - width) > kEps) return false;

            if (Mathf.Abs(oMax - max) < kEps && oMin > min + kEps) max = oMin;
            else if (Mathf.Abs(oMin - min) < kEps && oMax < max - kEps) min = oMax;
            else return false;

            Vector3 center = bb.center;
            Vector3 size = bb.size;
            if (alongZ) { center.z = (min + max) * 0.5f; size.z = max - min; }
            else { center.x = (min + max) * 0.5f; size.x = max - min; }
            SetWorldBox(b, center, size);
            return true;
        }

        //========================================
        // 床
        //========================================

        static int FixFloors(Transform stageRoot)
        {
            int count = 0;
            bool changed = true;
            // 切り抜いて増えた床どうしもまた調べる
            for (int guard = 0; changed && guard < 32; guard++)
            {
                changed = false;
                var floors = CollectFloors(stageRoot);
                for (int i = 0; i < floors.Count && !changed; i++)
                {
                    for (int j = i + 1; j < floors.Count && !changed; j++)
                    {
                        Bounds a = WorldBox(floors[i]);
                        Bounds b = WorldBox(floors[j]);
                        if (Mathf.Abs(a.max.y - b.max.y) > kEps) continue;
                        if (!OverlapXZ(a, b, out Rect o)) continue;
                        Subtract(floors[j], b, o);
                        changed = true;
                        count++;
                    }
                }
            }
            return count;
        }

        // 床 t(範囲 b)から重なり o を切り抜き、残りを最大4つの箱に分ける
        static void Subtract(Transform t, Bounds b, Rect o)
        {
            var pieces = new List<Rect>
            {
                Rect.MinMaxRect(b.min.x, b.min.z, o.xMin, b.max.z),   // 西
                Rect.MinMaxRect(o.xMax, b.min.z, b.max.x, b.max.z),   // 東
                Rect.MinMaxRect(o.xMin, b.min.z, o.xMax, o.yMin),     // 南
                Rect.MinMaxRect(o.xMin, o.yMax, o.xMax, b.max.z),     // 北
            };
            int index = 0;
            foreach (var p in pieces)
            {
                if (p.width < kEps || p.height < kEps) continue;
                var go = Object.Instantiate(t.gameObject, t.parent);
                go.name = $"{t.name}_{index++}";
                SetWorldBox(go.transform, new Vector3(p.center.x, b.center.y, p.center.y), new Vector3(p.width, b.size.y, p.height));
            }
            Object.DestroyImmediate(t.gameObject);
        }

        // 床: 回転していない動かない箱で、壁ではなく、背景の物でもないもの
        static List<Transform> CollectFloors(Transform stageRoot)
        {
            var list = new List<Transform>();
            foreach (var box in stageRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger || box.GetComponentInParent<Rigidbody>(true) != null) continue;
                if (box.GetComponentInParent<ReflectWall>(true) != null || box.GetComponentInParent<StageBackdrop>(true) != null) continue;
                if (box.GetComponentInParent<TumikiSpawnArea>(true) != null) continue;
                if (box.GetComponent<MeshRenderer>() == null || !IsAxisAlignedBox(box.transform)) continue;
                list.Add(box.transform);
            }
            return list;
        }

        //========================================
        // 補助
        //========================================

        static bool IsAxisAlignedBox(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube") return false;
            return Quaternion.Angle(t.rotation, Quaternion.identity) < 0.01f;
        }

        static Bounds WorldBox(Transform t)
        {
            Vector3 s = t.lossyScale;
            return new Bounds(t.position, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        static void SetWorldBox(Transform t, Vector3 center, Vector3 size)
        {
            Vector3 lossy = t.lossyScale;
            Vector3 local = t.localScale;
            t.position = center;
            t.localScale = new Vector3(local.x * size.x / lossy.x, local.y * size.y / lossy.y, local.z * size.z / lossy.z);
        }

        static bool OverlapXZ(Bounds a, Bounds b, out Rect o)
        {
            o = Rect.MinMaxRect(Mathf.Max(a.min.x, b.min.x), Mathf.Max(a.min.z, b.min.z), Mathf.Min(a.max.x, b.max.x), Mathf.Min(a.max.z, b.max.z));
            return o.width > kEps && o.height > kEps;
        }

        static bool OverlapY(Bounds a, Bounds b)
        {
            return Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > kEps;
        }
    }
}
