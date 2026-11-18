using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 割れる氷(Nakagawa.IceFloorGimmick。Stage21「氷割りゲーム」)の落ちたタイルを、CPU のナビメッシュに穴として反映する(2026-11-18)
    //  ・予兆(黄色く点滅)が始まってから戻りきるまで(Warning・Falling・Gone・Reviving)だけ、その場所を切り抜く
    //    (点滅中から道に使わない。上にいる CPU は、次に道を求めたときに外へ出る。中平が決めた 2026-11-18)
    //  ・切り抜きは六角形の内接円の円柱(NavMeshObstacle は箱か円柱だけ)。切り抜きはナビメッシュの体の半径ぶん広がるので、角も含めて穴になる
    //  ・タイル本体は落ちると動いて消えるので、切り抜きはタイルの子にせず、元の位置に置く
    // CpuNavMesh がナビメッシュを作るときに付ける(試合中だけの一時的なオブジェクト)
    public class CpuIceHoles : MonoBehaviour
    {
        Nakagawa.IceFloorGimmick m_ice;
        readonly List<NavMeshObstacle> m_obstacles = new List<NavMeshObstacle>();

        // ice の子として作る(すでに作ってあれば何もしない)
        public static void Attach(Nakagawa.IceFloorGimmick ice)
        {
            foreach (Transform child in ice.transform)
            {
                if (child.GetComponent<CpuIceHoles>() != null) return;
            }
            var go = new GameObject("CpuIceHoles");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(ice.transform, false);
            go.AddComponent<CpuIceHoles>().Setup(ice);
        }

        // 穴として切り抜くタイル(点滅中・落ちている・戻っている途中)
        public static bool IsHole(Nakagawa.IceFloorGimmick.Tile t)
        {
            var s = t.State;
            return s == Nakagawa.IceFloorGimmick.TileState.Warning
                || s == Nakagawa.IceFloorGimmick.TileState.Falling
                || s == Nakagawa.IceFloorGimmick.TileState.Gone
                || s == Nakagawa.IceFloorGimmick.TileState.Reviving;
        }

        // タイルの元の位置(上の面の中心。ワールド)
        public static Vector3 BaseWorldPosition(Nakagawa.IceFloorGimmick.Tile t)
        {
            Transform parent = t.Root != null ? t.Root.parent : null;
            return parent != null ? parent.TransformPoint(t.BasePosition) : t.BasePosition;
        }

        private void Setup(Nakagawa.IceFloorGimmick ice)
        {
            m_ice = ice;
            foreach (var t in ice.Tiles)
            {
                if (t.Solid || t.Root == null || t.Points == null || t.Points.Length < 3)
                {
                    m_obstacles.Add(null);
                    continue;
                }
                var go = new GameObject("Hole_" + t.Q + "_" + t.R);
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(transform, false);
                Vector3 top = BaseWorldPosition(t);
                Vector2 center = Centroid(t.Points);
                go.transform.position = new Vector3(top.x + center.x, top.y - t.Depth * 0.5f, top.z + center.y);
                go.transform.rotation = Quaternion.identity;

                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Capsule;
                obstacle.center = Vector3.zero;
                obstacle.radius = InnerRadius(t.Points, center);
                // 上の面から上はキャラの高さぶん、下は氷の厚みぶん
                obstacle.height = t.Depth + 2.0f * 2.0f;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                obstacle.enabled = false;
                m_obstacles.Add(obstacle);
            }
            Sync();
        }

        private void Update()
        {
            Sync();
        }

        private void Sync()
        {
            if (m_ice == null) return;
            var tiles = m_ice.Tiles;
            for (int i = 0; i < tiles.Count && i < m_obstacles.Count; i++)
            {
                var obstacle = m_obstacles[i];
                if (obstacle == null) continue;
                bool hole = IsHole(tiles[i]);
                if (obstacle.enabled != hole) obstacle.enabled = hole;
            }
        }

        public static Vector2 Centroid(Vector2[] points)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in points) sum += p;
            return sum / points.Length;
        }

        // 中心から一番近い辺までの距離(六角形なら内接円の半径)
        public static float InnerRadius(Vector2[] points, Vector2 center)
        {
            float best = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Length];
                Vector2 ab = b - a;
                float k = Mathf.Clamp01(Vector2.Dot(center - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
                best = Mathf.Min(best, Vector2.Distance(center, a + ab * k));
            }
            return best;
        }
    }
}
