using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 幽霊が壁にめり込む不具合の調査用。出ている幽霊を毎フレーム見張り、壁(ReflectWall)と重なっていた時間を記録する
    // (Unity MCP から GhostWallWatcher.Begin() で付ける。結果は Summary)
    public class GhostWallWatcher : MonoBehaviour
    {
        public static GhostWallWatcher Instance { get; private set; }

        // これより長く重なったままなら「抜け出せなかった」として Console に出す(秒)
        public float StuckTime = 1.0f;
        public int Frames { get; private set; }
        public int GhostFrames { get; private set; }
        public int OverlapFrames { get; private set; }
        public int StuckCount { get; private set; }
        public float MaxOverlapTime { get; private set; }

        readonly Dictionary<DarumaGhost, float> m_overlapTime = new Dictionary<DarumaGhost, float>();
        readonly Collider[] m_hits = new Collider[16];

        public static GhostWallWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("GhostWallWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GhostWallWatcher>();
            return Instance;
        }

        public string Summary => $"見張ったフレーム {Frames} / 幽霊が出ていたフレーム(のべ) {GhostFrames} / 壁と重なっていたフレーム {OverlapFrames}"
            + $" / 続けて重なっていた最長 {MaxOverlapTime:F2}秒 / 抜け出せなかった回数 {StuckCount}";

        private void LateUpdate()
        {
            Frames++;
            foreach (var ghost in DarumaGhost.Active)
            {
                if (ghost == null || ghost.IsSuspended) continue;
                GhostFrames++;
                bool overlap = Overlaps(ghost);
                m_overlapTime.TryGetValue(ghost, out float t);
                float before = t;
                t = overlap ? t + Time.deltaTime : 0.0f;
                m_overlapTime[ghost] = t;
                if (!overlap) continue;
                OverlapFrames++;
                if (t > MaxOverlapTime) MaxOverlapTime = t;
                if (before < StuckTime && t >= StuckTime)
                {
                    StuckCount++;
                    if (StuckCount <= 20) Debug.LogWarning($"[GhostWallWatcher] {ghost.name} が {StuckTime}秒以上壁と重なったまま pos={ghost.transform.position}");
                }
            }
        }

        // DarumaGhost の壁の判定と同じ球(少し小さめ)で調べる
        bool Overlaps(DarumaGhost ghost)
        {
            float r = ghost.Radius;
            Vector3 origin = ghost.transform.position + Vector3.up * (r + 0.05f);
            int count = Physics.OverlapSphereNonAlloc(origin, r - 0.02f, m_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (m_hits[i].GetComponentInParent<ReflectWall>() != null) return true;
            }
            return false;
        }
    }
}
