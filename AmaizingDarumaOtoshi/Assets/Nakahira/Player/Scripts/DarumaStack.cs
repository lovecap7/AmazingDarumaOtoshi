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

        // ストーリー用(Yurube): チュートリアルのCPUだけ、積み木の上限を下げる
        public void SetMaxCount(int max)
        {
            m_maxCount = Mathf.Max(1, max);
        }
        public float HeadRadius => m_headRadius;
        // 頭(王冠などの飾りを乗せる位置を読む。未設定なら null)
        public Transform Head => m_head;
        // 頭を含めたタワー全体の高さ
        public float Height => (NetCount >= 0 ? NetCount : Count) * TumikiBlock.kHeight + m_headRadius * 2.0f;

        // ネット対戦用(suehara): クライアントは積み木を持っていないので、ホストから届いた段数で高さを出す(-1 なら使わない。ローカルは -1 のまま)
        public int NetCount { get; set; } = -1;

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

        // 体の見た目の動き(DarumaMotion)。段・頭の本来の位置に、この揺れを足して置く(見た目だけ)。null なら揺らさない
        public IDarumaBodyMotion Motion { get; set; }

        // 段・頭ごとの「本来の位置」(揺れを足す前)と、最後に足した揺れ・最後に置いた位置
        struct Slot
        {
            public Vector3 Rest;
            public Vector3 Written;
            public Vector3 Offset;
        }
        readonly Dictionary<Transform, Slot> m_slots = new Dictionary<Transform, Slot>();
        readonly List<Transform> m_slotRemoved = new List<Transform>();
        readonly HashSet<Transform> m_slotUsed = new HashSet<Transform>();

        private void LateUpdate()
        {
            float step = m_fallSpeed * Time.deltaTime;
            m_slotUsed.Clear();
            for (int i = 0; i < m_blocks.Count; i++)
            {
                Transform t = m_blocks[i].transform;
                Vector3 target = new Vector3(0.0f, TumikiBlock.kHeight * (i + 0.5f), 0.0f);
                Place(t, target, step);
            }

            if (m_head != null)
            {
                Vector3 headTarget = new Vector3(0.0f, m_blocks.Count * TumikiBlock.kHeight + m_headRadius, 0.0f);
                Place(m_head, headTarget, step, true);
            }

            // 段から外れた積み木の記録を消す
            if (m_slots.Count > m_slotUsed.Count)
            {
                m_slotRemoved.Clear();
                foreach (var key in m_slots.Keys) if (!m_slotUsed.Contains(key)) m_slotRemoved.Add(key);
                foreach (var key in m_slotRemoved) m_slots.Remove(key);
            }
        }

        // 本来の位置を target へ寄せ、揺れを足して置く
        // 外から位置を変えられていたら(拾ったときの KeepVisualsInPlace など)、そこを本来の位置として続ける
        // 揺れがないとき(Motion が null)は前と同じ置き方になる
        private void Place(Transform t, Vector3 target, float step, bool isHead = false)
        {
            m_slotUsed.Add(t);
            Vector3 current = t.localPosition;
            bool known = m_slots.TryGetValue(t, out Slot slot);
            if (!known || slot.Written != current)
            {
                // 外から動かされた分はそのまま本来の位置へ移す(前に足した揺れだけを引く)
                slot.Rest = current - (known ? slot.Offset : Vector3.zero);
            }
            slot.Rest = Vector3.MoveTowards(slot.Rest, target, step);

            // 揺れは平行にずらすだけ(積み木は傾けない)
            Vector3 offset = Motion != null ? Motion.GetOffset(slot.Rest.y) : Vector3.zero;

            t.localPosition = slot.Rest + offset;
            // 段は前と同じく、まっすぐに直す(頭の回転は前から触っていない)
            if (!isHead) t.localRotation = Quaternion.identity;
            slot.Written = t.localPosition;
            slot.Offset = offset;
            m_slots[t] = slot;
        }
    }
}
