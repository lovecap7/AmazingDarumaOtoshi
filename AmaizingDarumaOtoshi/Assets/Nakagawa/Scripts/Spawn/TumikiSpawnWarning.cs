using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木を生成する前の予告。床に平らな印(積み木の色)を出し、時間が来たらその位置に積み木を生成して消える
    //  ・外周の輪が点滅しながら(終わりに近づくほど速く)、中の円が中心から広がり、広がり切ったら生成する
    //  ・予告中の位置は他の生成でも使わない(TumikiSpawnArea が Active を読んで避ける)
    //  ・生成するエリアが無効になったら(ステージの切り替えなど)、生成せずに消える
    //  ・生成する直前にエリアに置けるか確かめ直す(TumikiSpawnArea.CanSpawnAt。プレイヤーや積み木が乗っていれば生成しない)
    public class TumikiSpawnWarning : MonoBehaviour
    {
        // 印の大きさ(積み木の半径に対する倍率)
        const float kSizeRate = 1.1f;
        // 床から浮かせる高さ(床とちらつかないように)
        const float kFloorOffset = 0.02f;
        // 出てくるときに輪が縮む時間
        const float kIntroTime = 0.15f;

        static readonly List<TumikiSpawnWarning> s_active = new List<TumikiSpawnWarning>();

        // 予告中のもの
        public static IReadOnlyList<TumikiSpawnWarning> Active => s_active;
        // 予告どおりに生成した(演出・効果音などの拡張用)
        public static event Action<TumikiBlock> AnySpawned;
        // 置けなくなって生成しなかった
        public static event Action<TumikiSpawnWarning> AnyCanceled;

        // 生成する積み木の中心
        public Vector3 Position { get; private set; }
        public TumikiColor Color { get; private set; }
        public float Duration { get; private set; }
        public float Progress => Duration > 0.0f ? Mathf.Clamp01(m_time / Duration) : 1.0f;

        TumikiSpawnArea m_area;
        TumikiBlock m_prefab;
        Quaternion m_rotation;
        float m_time;
        Renderer m_fill, m_ring, m_edge;
        Color m_color;

        // ドメインの再読み込みをしない設定でも、再生のたびに空にする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_active.Clear();

        public static TumikiSpawnWarning Create(TumikiSpawnArea area, TumikiBlock prefab, TumikiColor color, Vector3 pos, Quaternion rot, float duration)
        {
            var go = new GameObject($"TumikiSpawnWarning_{color}");
            go.transform.position = new Vector3(pos.x, area.FloorY + kFloorOffset, pos.z);
            var w = go.AddComponent<TumikiSpawnWarning>();
            w.Setup(area, prefab, color, pos, rot, duration);
            return w;
        }

        private void Setup(TumikiSpawnArea area, TumikiBlock prefab, TumikiColor color, Vector3 pos, Quaternion rot, float duration)
        {
            m_area = area;
            m_prefab = prefab;
            m_rotation = rot;
            Position = pos;
            Color = color;
            Duration = Mathf.Max(0.0f, duration);
            m_color = TumikiColorUtil.ToUnityColor(color);

            m_fill = EffectMeshes.Create("Fill", transform, EffectMeshes.Disc, false);
            m_edge = EffectMeshes.Create("Edge", transform, EffectMeshes.Disc, false);
            m_ring = EffectMeshes.Create("Ring", transform, EffectMeshes.Ring, true);
            m_ring.transform.localPosition = Vector3.up * 0.005f;
            Apply();
        }

        private void OnEnable() => s_active.Add(this);
        private void OnDisable() => s_active.Remove(this);

        private void Update()
        {
            if (m_area == null || !m_area.isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }

            m_time += Time.deltaTime;
            if (m_time >= Duration)
            {
                Finish();
                return;
            }
            Apply();
        }

        private void Finish()
        {
            if (m_area.CanSpawnAt(Position))
            {
                var block = m_area.SpawnBlock(m_prefab, Color, Position, m_rotation);
                if (block != null) AnySpawned?.Invoke(block);
            }
            else
            {
                AnyCanceled?.Invoke(this);
            }
            Destroy(gameObject);
        }

        private void Apply()
        {
            float t = Progress;
            float size = TumikiBlock.kRadius * kSizeRate;
            float intro = Mathf.Clamp01(m_time / kIntroTime);

            // 下地: 印の範囲をうっすら塗る
            m_edge.transform.localScale = new Vector3(size, 1.0f, size);
            EffectMeshes.SetColor(m_edge, WithAlpha(m_color, 0.18f * intro));

            // 中の円: 中心から広がる(広がり切ったら生成)
            float fill = size * (1.0f - Mathf.Pow(1.0f - t, 2.0f));
            m_fill.transform.localScale = new Vector3(fill, 1.0f, fill);
            EffectMeshes.SetColor(m_fill, WithAlpha(m_color, Mathf.Lerp(0.35f, 0.65f, t)));

            // 外周の輪: 外から縮んで現れ、終わりに近づくほど速く点滅する
            float ring = size * Mathf.Lerp(1.6f, 1.0f, 1.0f - (1.0f - intro) * (1.0f - intro));
            m_ring.transform.localScale = new Vector3(ring, 1.0f, ring);
            float blink = 0.5f + 0.5f * Mathf.Cos(m_time * Mathf.Lerp(8.0f, 30.0f, t));
            EffectMeshes.SetColor(m_ring, WithAlpha(EffectMeshes.Brighten(m_color, 0.4f), intro * Mathf.Lerp(0.55f, 1.0f, blink)));
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
