using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Nakahira.DebugTools
{
    // 高く飛び上がる不具合の調査用(HighJumpBugTest シーン専用)
    // 再生すると、いくつかの状況を横に並べて同時に作り、キャラクターの一番高い位置と上向きの速さを記録して Console に出す
    public class HighJumpBugProbe : MonoBehaviour
    {
        [SerializeField] DarumaCharacter m_characterPrefab;
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [SerializeField] float m_recordTime = 3.0f;

        class Case
        {
            public string Name;
            public DarumaCharacter Character;
            public float StartY;
            public float MaxY;
            public float MaxVy;
            public int TraceCount;
        }

        readonly List<Case> m_cases = new List<Case>();

        IEnumerator Start()
        {
            // キャラクターが接地して落ち着くのを待つ
            var a = Spawn("A: 重なった積み木のみ", 0);
            var b = Spawn("B: 重なった積み木 + ヒットストップ0.1", 1);
            var c = Spawn("C: 重なった積み木 + ヒットストップ0.01", 2);
            var d = Spawn("D: ヒットストップ0.01のみ(重なりなし)", 3);
            var e = Spawn("E: 3段積み、真ん中に弾が当たる", 4);
            var f = Spawn("F: 3段積み、一番下をハンマーで弾かれる(0.01)", 5);
            var gAttacker = Spawn("G: 殴り合い(殴る側)", 6);
            var gVictim = Spawn("G: 殴り合い(殴られる側)", 6, new Vector3(0.0f, 0.0f, 1.3f));
            var h = Spawn("H: 2m から落ちて、スロー0.1 の間に着地(上がった高さは 2m なら正常)", 7);
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < 3; i++) gVictim.Character.Stack.AddBottom(NewBlock(gVictim.Character.transform.position));
            for (int i = 0; i < 3; i++) gAttacker.Character.Stack.AddBottom(NewBlock(gAttacker.Character.transform.position));

            for (int i = 0; i < 3; i++) e.Character.Stack.AddBottom(NewBlock(e.Character.transform.position));
            for (int i = 0; i < 3; i++) f.Character.Stack.AddBottom(NewBlock(f.Character.transform.position));
            yield return new WaitForSeconds(0.5f);

            foreach (var cs in m_cases) { cs.StartY = cs.Character.Rigidbody.position.y; cs.MaxY = cs.StartY; }

            // A〜C: キャラクターの足元に積み木を重ねて出す
            NewBlock(a.Character.transform.position + Vector3.up * 0.25f);
            NewBlock(b.Character.transform.position + Vector3.up * 0.25f);
            b.Character.StartHitStop(1.0f, 0.1f, 0.0f);
            NewBlock(c.Character.transform.position + Vector3.up * 0.25f);
            c.Character.StartHitStop(2.0f, 0.01f, 0.0f);
            d.Character.StartHitStop(2.0f, 0.01f, 0.0f);

            // E: 横から真ん中の段の高さに弾を撃つ
            Vector3 ep = e.Character.transform.position;
            var proj = NewBlock(ep + new Vector3(-3.0f, 1.25f, 0.0f));
            proj.Launch(Vector3.right, 12.0f, null);

            // F: 一番下を弾く(ハンマーと同じ呼び方)
            f.Character.KnockBottom(Vector3.forward, 10.0f, null, out TumikiBlock knocked);
            f.Character.StartHitStop(2.0f, 0.01f, 0.0f);
            if (knocked != null) knocked.StartHitStop(2.0f, 0.01f, 0.0f);

            // G: 2人の足元に積み木を重ねて散らし、ハンマーで殴る(何回か振る)
            Vector3 gp = gAttacker.Character.transform.position;
            NewBlock(gp + new Vector3(0.3f, 0.25f, 0.0f));
            NewBlock(gp + new Vector3(-0.3f, 0.25f, 1.3f));
            NewBlock(gp + new Vector3(0.0f, 0.25f, 0.65f));
            StartCoroutine(SweepRepeatedly(gAttacker.Character, 4));

            // H: 落ちている最中にスロー
            h.Character.Teleport(h.Character.transform.position + Vector3.up * 2.0f, 0.0f);
            h.Character.StartHitStop(1.5f, 0.1f, 0.0f);

            float t = 0.0f;
            while (t < m_recordTime)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                foreach (var cs in m_cases)
                {
                    if (cs.Character == null) continue;
                    cs.MaxY = Mathf.Max(cs.MaxY, cs.Character.Rigidbody.position.y);
                    float vy = cs.Character.Rigidbody.linearVelocity.y;
                    cs.MaxVy = Mathf.Max(cs.MaxVy, vy);
                    if (vy > 0.5f && cs.TraceCount < 6)
                    {
                        cs.TraceCount++;
                        Debug.Log($"[HighJumpBugProbe:trace] {cs.Name} t={t:F2} y={cs.Character.Rigidbody.position.y:F2} vy={vy:F2}"
                            + $" scale={cs.Character.LocalTimeScale:F2} grounded={cs.Character.IsGrounded} stack={cs.Character.Stack.Count}");
                    }
                }
            }

            foreach (var cs in m_cases)
            {
                string end = cs.Character == null ? " (脱落)" : $" / 最後の高さ {cs.Character.Rigidbody.position.y:F2}m";
                Debug.Log($"[HighJumpBugProbe] {cs.Name}: 上がった高さ {cs.MaxY - cs.StartY:F2}m / 最大の上向き速度 {cs.MaxVy:F2}m/s" + end);
            }
        }

        IEnumerator SweepRepeatedly(DarumaCharacter attacker, int count)
        {
            for (int i = 0; i < count && attacker != null; i++)
            {
                attacker.Hammer.TrySweep();
                yield return new WaitForSeconds(0.6f);
            }
        }

        Case Spawn(string name, int index, Vector3 offset = default)
        {
            var ch = Instantiate(m_characterPrefab, new Vector3(index * 3.5f - 12.0f, 0.0f, 0.0f) + offset, Quaternion.identity);
            ch.name = name;
            var cs = new Case { Name = name, Character = ch };
            m_cases.Add(cs);
            return cs;
        }

        TumikiBlock NewBlock(Vector3 position)
        {
            return Instantiate(m_tumikiPrefab, position, Quaternion.identity);
        }
    }
}
