using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 積み木が壁にめり込む不具合の調査用。落ちている・飛んでいる積み木を物理ステップごとに見張り、
    // 壁(ReflectWall)にめり込んでいた時間と、ステージの範囲の外(外壁の外側)へ出た回数を記録する
    // (Unity MCP から TumikiWallWatcher.Begin() で付ける。結果は Summary)
    public class TumikiWallWatcher : MonoBehaviour
    {
        public static TumikiWallWatcher Instance { get; private set; }

        // これより深くめり込んでいたら数える(m)
        public float Depth = 0.1f;
        // これより長くめり込んだままなら「抜け出せなかった」として Console に出す(秒)
        public float StuckTime = 0.5f;
        // これより長くめり込んだら、場所・壁・状態を Console に出す(秒)
        public float LogTime = 0.1f;
        int m_logged;
        // この積み木だけ、TraceSteps ステップの間、毎ステップの位置・めり込み・速度・飛ぶ向きを出す
        public TumikiBlock TraceBlock;
        public int TraceSteps = 30;
        public int Steps { get; private set; }
        public int BlockSteps { get; private set; }
        public int EmbeddedSteps { get; private set; }
        public int StuckCount { get; private set; }
        public float MaxEmbeddedTime { get; private set; }
        public int OutsideCount { get; private set; }

        readonly Dictionary<TumikiBlock, float> m_embeddedTime = new Dictionary<TumikiBlock, float>();
        readonly HashSet<TumikiBlock> m_outside = new HashSet<TumikiBlock>();
        readonly Collider[] m_hits = new Collider[16];

        public static TumikiWallWatcher Begin()
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var go = new GameObject("TumikiWallWatcher");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TumikiWallWatcher>();
            return Instance;
        }

        public string Summary => $"見張ったステップ {Steps} / 積み木(のべ) {BlockSteps} / めり込んでいたステップ {EmbeddedSteps}"
            + $" / 続けてめり込んでいた最長 {MaxEmbeddedTime:F2}秒 / 抜け出せなかった回数 {StuckCount} / 地面の高さでステージの範囲の外に出た積み木 {OutsideCount}";

        private void FixedUpdate()
        {
            Steps++;
            StageArea area = DarumaMatch.Current != null ? DarumaMatch.Current.StageArea : null;
            foreach (var block in TumikiBlock.All)
            {
                if (block == null || block.CurrentState == TumikiBlock.State.Stacked) continue;
                var col = block.GetComponent<Collider>();
                if (col == null || !col.enabled || block.Rigidbody.isKinematic) continue;
                BlockSteps++;

                Vector3 pos = block.Rigidbody.position;
                float depth = Penetration(block, col, pos, out Collider deepestWall);
                if (block == TraceBlock && TraceSteps-- > 0)
                {
                    Debug.Log($"[TumikiWallWatcher:trace] step={Steps} pos={pos} depth={depth:F2} v={block.Rigidbody.linearVelocity}"
                        + $" dir={block.Direction} speed={block.Speed:F1} state={block.CurrentState}");
                }
                m_embeddedTime.TryGetValue(block, out float t);
                float before = t;
                t = depth > Depth ? t + Time.fixedDeltaTime : 0.0f;
                m_embeddedTime[block] = t;
                if (depth > Depth)
                {
                    EmbeddedSteps++;
                    if (t > MaxEmbeddedTime) MaxEmbeddedTime = t;
                    // 0.1 秒続いたら、どこで何にめり込んだかを 1 回だけ出す
                    if (before < LogTime && t >= LogTime && m_logged++ < 30)
                    {
                        string wallName = deepestWall != null ? $"{deepestWall.transform.parent?.name}/{deepestWall.name}" : "?";
                        string stage = deepestWall != null ? deepestWall.transform.root.name : "?";
                        Debug.LogWarning($"[TumikiWallWatcher:detail] {stage} {wallName} pos={pos} depth={depth:F2} state={block.CurrentState}"
                            + $" v={block.Rigidbody.linearVelocity} scale={block.GetComponent<HitStop>().TimeScale:F2}");
                    }
                    if (before < StuckTime && t >= StuckTime)
                    {
                        StuckCount++;
                        if (StuckCount <= 20) Debug.LogWarning($"[TumikiWallWatcher] {block.name} が {StuckTime}秒以上壁にめり込んだまま pos={pos} depth={depth:F2}");
                    }
                }

                // 床の高さにいるのにステージの範囲の外 = 外壁の外側へ抜けた(奈落へ落ちている途中は数えない)
                if (area != null && pos.y > area.FloorY - 0.1f)
                {
                    Vector3 c = area.Clamp(pos, 0.0f);
                    bool outside = Mathf.Abs(c.x - pos.x) > 0.01f || Mathf.Abs(c.z - pos.z) > 0.01f;
                    if (outside && m_outside.Add(block))
                    {
                        OutsideCount++;
                        if (OutsideCount <= 20) Debug.LogWarning($"[TumikiWallWatcher] {block.name} がステージの範囲の外に出た pos={pos}");
                    }
                }
            }
        }

        float Penetration(TumikiBlock block, Collider col, Vector3 pos, out Collider deepestWall)
        {
            deepestWall = null;
            Quaternion rot = block.Rigidbody.rotation;
            Vector3 half = new Vector3(TumikiBlock.kRadius, TumikiBlock.kHeight * 0.5f, TumikiBlock.kRadius);
            int count = Physics.OverlapBoxNonAlloc(pos, half, m_hits, rot, ~0, QueryTriggerInteraction.Ignore);
            float deepest = 0.0f;
            for (int i = 0; i < count; i++)
            {
                Collider wall = m_hits[i];
                if (wall == col || wall.GetComponentInParent<ReflectWall>() == null) continue;
                if (Physics.ComputePenetration(col, pos, rot, wall, wall.transform.position, wall.transform.rotation, out _, out float dist))
                {
                    if (dist > deepest) { deepest = dist; deepestWall = wall; }
                }
            }
            return deepest;
        }
    }
}
