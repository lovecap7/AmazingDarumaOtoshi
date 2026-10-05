using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // プレイヤーに積まれた積み木の管理
    // index 0 が一番下(足元)、末尾が一番上(頭のすぐ下 = 一番古い段)
    public class DarumaStack : MonoBehaviour
    {
        [SerializeField] int m_maxCount = 10;
        [SerializeField] Transform m_head;
        [SerializeField] float m_headRadius = 0.45f;
        // 段が抜けたとき、上の段が落ちてくる速さ
        [SerializeField] float m_fallSpeed = 8.0f;

        readonly List<TumikiBlock> m_blocks = new List<TumikiBlock>();

        public int Count => m_blocks.Count;
        public int MaxCount => m_maxCount;
        public float HeadRadius => m_headRadius;
        // 頭を含めたタワー全体の高さ
        public float Height => Count * TumikiBlock.kHeight + m_headRadius * 2.0f;

        // 段数が変わったとき
        public event Action Changed;

        // 一番下に積む。上限を超えたら一番古い段を外して返す(なければnull)
        public TumikiBlock AddBottom(TumikiBlock block)
        {
            block.SetStacked(transform);
            m_blocks.Insert(0, block);

            TumikiBlock overflow = null;
            if (m_blocks.Count > m_maxCount)
            {
                int top = m_blocks.Count - 1;
                overflow = m_blocks[top];
                m_blocks.RemoveAt(top);
                overflow.SetLoose();
            }

            Changed?.Invoke();
            return overflow;
        }

        // 指定した段を抜き取る(上の段は落ちてくる)
        public TumikiBlock RemoveAt(int index)
        {
            TumikiBlock block = m_blocks[index];
            m_blocks.RemoveAt(index);
            block.SetLoose();
            Changed?.Invoke();
            return block;
        }

        public TumikiBlock Get(int index) => m_blocks[index];

        // プレイヤーがrootDeltaだけ移動したとき、頭と積まれた段のワールド位置を移動前のまま保つ
        public void KeepVisualsInPlace(Vector3 rootDelta)
        {
            foreach (var b in m_blocks)
            {
                b.transform.position -= rootDelta;
            }
            if (m_head != null)
            {
                m_head.position -= rootDelta;            }
        }

        // 足元からの高さが何段目に当たるか。Count以上なら頭
        public int IndexFromHeight(float heightFromFeet)
        {
            return Mathf.Max(0, Mathf.FloorToInt(heightFromFeet / TumikiBlock.kHeight));
        }

        // 指定した色の積み木の数(キャラクターのパッシブ用)
        public int CountColor(TumikiColor color)
        {
            int n = 0;
            foreach (var b in m_blocks)
            {
                if (b.Color == color) n++;
            }
            return n;
        }

        private void LateUpdate()
        {
            float step = m_fallSpeed * Time.deltaTime;
            for (int i = 0; i < m_blocks.Count; i++)
            {
                Transform t = m_blocks[i].transform;
                Vector3 target = new Vector3(0.0f, TumikiBlock.kHeight * (i + 0.5f), 0.0f);
                t.localPosition = Vector3.MoveTowards(t.localPosition, target, step);
                t.localRotation = Quaternion.identity;
            }

            if (m_head != null)
            {
                Vector3 headTarget = new Vector3(0.0f, m_blocks.Count * TumikiBlock.kHeight + m_headRadius, 0.0f);
                m_head.localPosition = Vector3.MoveTowards(m_head.localPosition, headTarget, step);
            }
        }
    }
}
