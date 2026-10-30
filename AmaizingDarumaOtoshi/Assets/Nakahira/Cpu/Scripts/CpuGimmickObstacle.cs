using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 動く・出たり消えたりするギミック(せり上がり塀・扇風機の本体)の当たり判定を、CPU のナビメッシュに穴として反映する
    // ナビメッシュは焼き込み直さず(重いので)、当たり判定と同じ箱の NavMeshObstacle でその場所だけ切り抜く
    //  ・当たり判定が有効な間だけ切り抜く(せり上がり塀は、せり上がり始めてから床に戻るまで)
    //  ・動いても毎フレーム切り抜き直す(扇風機の往復)
    // CpuNavMesh がナビメッシュを作るときに、当たり判定の子として作る(試合中だけの一時的なオブジェクト)
    public class CpuGimmickObstacle : MonoBehaviour
    {
        // 切り抜き直す移動量(m)
        const float kMoveThreshold = 0.1f;
        // 当たり判定より水平にこの距離(m)だけ広く切り抜く
        // (塀と壁の間の、体がぎりぎり通る隙間を道にしない。押し合って引っかかるので)
        const float kMargin = 0.2f;

        BoxCollider m_source;
        NavMeshObstacle m_obstacle;

        public BoxCollider Source => m_source;

        // source の子として作る(すでに作ってあれば何もしない)
        public static void Attach(BoxCollider source)
        {
            foreach (Transform child in source.transform)
            {
                var existing = child.GetComponent<CpuGimmickObstacle>();
                if (existing != null && existing.m_source == source) return;
            }
            var go = new GameObject("CpuNavObstacle");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(source.transform, false);
            var self = go.AddComponent<CpuGimmickObstacle>();
            self.Setup(source);
        }

        private void Setup(BoxCollider source)
        {
            m_source = source;
            m_obstacle = gameObject.AddComponent<NavMeshObstacle>();
            m_obstacle.shape = NavMeshObstacleShape.Box;
            // 当たり判定と同じ箱(親の当たり判定の座標で。拡大は親から受け継ぐ)
            m_obstacle.center = source.center;
            Vector3 scale = source.transform.lossyScale;
            m_obstacle.size = source.size + new Vector3(
                kMargin * 2.0f / Mathf.Max(0.01f, Mathf.Abs(scale.x)), 0.0f,
                kMargin * 2.0f / Mathf.Max(0.01f, Mathf.Abs(scale.z)));
            m_obstacle.carving = true;
            m_obstacle.carveOnlyStationary = false;
            m_obstacle.carvingMoveThreshold = kMoveThreshold;
            Sync();
        }

        private void Update()
        {
            Sync();
        }

        private void Sync()
        {
            if (m_obstacle == null) return;
            bool solid = m_source != null && m_source.enabled;
            if (m_obstacle.enabled != solid) m_obstacle.enabled = solid;
        }
    }
}
