using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube.Lab
{
    // ラボ用の積み木操作。色と数をその場で用意する。
    // ステージの TumikiSpawner は使わない(色が出るまで待たなくてよいように)。
    public class CharacterLabBlocks : MonoBehaviour
    {
        GameObject m_blockPrefab;
        readonly List<TumikiBlock> m_spawned = new List<TumikiBlock>();

        public void Initialize(GameObject blockPrefab)
        {
            m_blockPrefab = blockPrefab;
        }

        public int SpawnedCount => m_spawned.Count;

        // キャラクターの一番下に積む(上限を超えた分は消す)
        public void AddToStack(DarumaCharacter target, TumikiColor color, int count)
        {
            if (target == null || target.Stack == null || m_blockPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                var block = InstantiateBlock(target.transform.position + Vector3.up * 0.5f);
                if (block == null) return;
                block.SetColor(color);
                var overflow = target.Stack.AddBottom(block);
                if (overflow != null)
                {
                    m_spawned.Remove(overflow);
                    Destroy(overflow.gameObject);
                }
                m_spawned.Add(block);
            }
        }

        // 積んでいる積み木を全部消す
        public void ClearStack(DarumaCharacter target)
        {
            if (target == null || target.Stack == null) return;
            while (target.Stack.Count > 0)
            {
                var block = target.Stack.RemoveAt(0);
                if (block == null) continue;
                m_spawned.Remove(block);
                Destroy(block.gameObject);
            }
        }

        // 指定した色の積み木を1個だけ消す(上から探す)
        public bool RemoveFromStack(DarumaCharacter target, TumikiColor color)
        {
            if (target == null || target.Stack == null) return false;
            for (int i = target.Stack.Count - 1; i >= 0; i--)
            {
                var block = target.Stack.Get(i);
                if (block == null || block.Color != color) continue;
                target.Stack.RemoveAt(i);
                m_spawned.Remove(block);
                Destroy(block.gameObject);
                return true;
            }
            return false;
        }

        // キャラクターの正面から、指定した向きへ積み木を飛ばす
        public void Launch(DarumaCharacter owner, TumikiColor color, Vector3 direction, float speed)
        {
            if (owner == null || m_blockPrefab == null) return;
            Vector3 dir = new Vector3(direction.x, 0.0f, direction.z);
            if (dir.sqrMagnitude < 0.0001f) dir = owner.transform.forward;
            dir.Normalize();

            Vector3 position = owner.transform.position
                + Vector3.up * (TumikiBlock.kHeight * 0.5f)
                + dir * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
            var block = InstantiateBlock(position);
            if (block == null) return;
            block.SetColor(color);
            block.transform.rotation = Quaternion.LookRotation(dir);
            block.Launch(dir, speed, owner);
            m_spawned.Add(block);
        }

        // 積まれていない(落ちている・飛んでいる)積み木だけを消す
        public void DestroyLoose()
        {
            var blocks = FindObjectsByType<TumikiBlock>();
            foreach (var block in blocks)
            {
                if (block == null || block.CurrentState == TumikiBlock.State.Stacked) continue;
                Destroy(block.gameObject);
            }
            m_spawned.RemoveAll(b => b == null);
        }

        public int CountFlying()
        {
            int count = 0;
            var blocks = FindObjectsByType<TumikiBlock>();
            foreach (var block in blocks)
            {
                if (block != null && block.CurrentState == TumikiBlock.State.Flying) count++;
            }
            return count;
        }

        TumikiBlock InstantiateBlock(Vector3 position)
        {
            var go = Instantiate(m_blockPrefab, position, Quaternion.identity);
            var block = go.GetComponent<TumikiBlock>();
            if (block == null)
            {
                Destroy(go);
                return null;
            }
            return block;
        }
    }
}
