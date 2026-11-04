using System.Collections.Generic;
using Nakagawa;
using UnityEngine;

namespace Nakahira
{
    // 魂(青白い人魂)の見た目。幽霊が出るとき(GhostAppearEffect)と復活するとき(GhostReviveSoulEffect)で使う
    //  ・丸い頭 + 先が細く透明になるしっぽのメッシュ(外側の光と芯の2枚)、軌跡、光源
    //  ・しっぽは進む向きの反対へ伸び、速いほど長くなる。炎のようにゆらめく
    // 見た目の値は GhostAppearParams の「魂の見た目」「光源」
    public class GhostSoulVisual
    {
        // メッシュのしっぽの長さ(頭の半径を1として)
        const float kTailLength = 2.2f;
        // これ以上の速さでしっぽが一番伸びる(m/秒)
        const float kStretchSpeed = 40.0f;

        static Mesh s_mesh;

        readonly GhostAppearParams m_params;
        readonly Transform m_root;
        readonly Transform m_body;
        readonly MeshRenderer m_glow, m_core;
        readonly TrailRenderer m_trail;
        readonly Light m_light;
        Vector3 m_lastPos;
        Vector3 m_dir = Vector3.up;
        float m_stretch = 1.0f;

        public Transform Root => m_root;
        public Vector3 Position => m_root.position;

        // parent の子として作る(光源も一緒に動く)
        public GhostSoulVisual(Transform parent, GhostAppearParams p)
        {
            m_params = p;
            m_root = new GameObject("Soul").transform;
            m_root.SetParent(parent, false);
            m_body = new GameObject("Body").transform;
            m_body.SetParent(m_root, false);

            // 外側の光(大きめ・薄め)と芯。芯を手前に描く
            m_glow = EffectMeshes.Create("Glow", m_body, Mesh, false);
            m_glow.transform.localScale = Vector3.one * 1.6f;
            m_glow.sortingOrder = 0;
            m_core = EffectMeshes.Create("Core", m_body, Mesh, false);
            m_core.sortingOrder = 1;

            m_trail = m_root.gameObject.AddComponent<TrailRenderer>();
            m_trail.sharedMaterial = EffectMeshes.AlphaMaterial;
            m_trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_trail.receiveShadows = false;
            m_trail.minVertexDistance = 0.05f;
            m_trail.numCapVertices = 4;
            m_trail.widthCurve = new AnimationCurve(new Keyframe(0.0f, 1.0f), new Keyframe(1.0f, 0.0f));
            m_trail.time = p.TrailTime;
            m_trail.widthMultiplier = p.TrailWidth;

            if (p.LightIntensity > 0.0f)
            {
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(m_root, false);
                m_light = lightGo.AddComponent<Light>();
                m_light.type = LightType.Point;
                m_light.shadows = LightShadows.None;
                m_light.color = p.SoulColor;
                m_light.range = p.LightRange;
                m_light.intensity = 0.0f;
            }
        }

        public void SetActive(bool active) => m_root.gameObject.SetActive(active);

        // 動かす。jump なら瞬間移動(軌跡を消す。向きと伸びはそのまま)
        public void Move(Vector3 pos, bool jump)
        {
            if (!jump)
            {
                Vector3 delta = pos - m_lastPos;
                if (delta.sqrMagnitude > 0.000001f) m_dir = delta.normalized;
                float dt = Time.deltaTime;
                float speed = dt > 0.0f ? delta.magnitude / dt : 0.0f;
                m_stretch = Mathf.Lerp(1.0f, m_params.TailStretch, Mathf.Clamp01(speed / kStretchSpeed));
            }
            m_root.position = pos;
            m_lastPos = pos;
            if (jump) m_trail.Clear();
        }

        // 見た目を更新する。size: 大きさの倍率 / alpha: 濃さの倍率 / time: ゆらめきに使う時間
        public void Apply(float size, float alpha, float time)
        {
            var p = m_params;
            float flicker = Mathf.Sin(time * p.FlickerSpeed);
            float r = p.SoulRadius * size * (1.0f + 0.08f * flicker);

            // しっぽは進む向きの反対へ。炎のようにゆらゆら揺らす
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, -m_dir);
            Quaternion sway = Quaternion.Euler(
                Mathf.Sin(time * p.FlickerSpeed * 0.7f) * 10.0f, 0.0f,
                Mathf.Cos(time * p.FlickerSpeed * 0.9f) * 10.0f);
            m_body.rotation = rot * sway;
            m_body.localScale = new Vector3(r, r * p.TailLength * m_stretch * (1.0f - 0.06f * flicker), r);

            Color glow = p.SoulColor;
            glow.a = 0.45f * alpha;
            EffectMeshes.SetColor(m_glow, glow);
            Color core = p.CoreColor;
            core.a = alpha;
            EffectMeshes.SetColor(m_core, core);

            m_trail.time = p.TrailTime;
            m_trail.widthMultiplier = p.TrailWidth * size;
            m_trail.colorGradient = TrailGradient(p.SoulColor, 0.7f * alpha);

            if (m_light != null)
            {
                m_light.color = p.SoulColor;
                m_light.range = p.LightRange;
                m_light.intensity = p.LightIntensity * alpha;
            }
        }

        private static Gradient TrailGradient(Color color, float alpha)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(color, 0.0f), new GradientColorKey(color, 1.0f) },
                new[] { new GradientAlphaKey(alpha, 0.0f), new GradientAlphaKey(0.0f, 1.0f) });
            return g;
        }

        //========================================
        // 人魂のメッシュ(丸い頭 + 先が細く透明になるしっぽ。しっぽは +Y 向き)
        //========================================

        static Mesh Mesh => s_mesh != null ? s_mesh : s_mesh = BuildMesh();

        static Mesh BuildMesh()
        {
            const int seg = 20;
            const int headRings = 8;
            const int tailRings = 12;

            // 輪ごとの (高さ, 半径, 濃さ)
            var rings = new List<Vector3>();
            for (int i = 1; i <= headRings; i++)
            {
                float a = i / (float)headRings * Mathf.PI * 0.5f;
                rings.Add(new Vector3(-Mathf.Cos(a), Mathf.Sin(a), 1.0f));
            }
            for (int i = 1; i < tailRings; i++)
            {
                float s = i / (float)tailRings;
                float radius = Mathf.Pow(Mathf.Cos(s * Mathf.PI * 0.5f), 0.8f);
                rings.Add(new Vector3(s * kTailLength, radius, Mathf.Pow(1.0f - s, 1.3f)));
            }

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            // 頭の先
            v.Add(new Vector3(0.0f, -1.0f, 0.0f));
            c.Add(Color.white);
            for (int r = 0; r < rings.Count; r++)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2.0f / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * rings[r].y, rings[r].x, Mathf.Sin(a) * rings[r].y));
                    c.Add(new Color(1.0f, 1.0f, 1.0f, rings[r].z));
                }
            }
            // しっぽの先
            int tip = v.Count;
            v.Add(new Vector3(0.0f, kTailLength, 0.0f));
            c.Add(new Color(1.0f, 1.0f, 1.0f, 0.0f));

            for (int i = 0; i < seg; i++)
            {
                t.Add(0); t.Add(1 + (i + 1) % seg); t.Add(1 + i);
            }
            for (int r = 0; r < rings.Count - 1; r++)
            {
                int b0 = 1 + r * seg;
                int b1 = b0 + seg;
                for (int i = 0; i < seg; i++)
                {
                    int i0 = b0 + i, i1 = b0 + (i + 1) % seg;
                    int j0 = b1 + i, j1 = b1 + (i + 1) % seg;
                    t.Add(i0); t.Add(j0); t.Add(i1);
                    t.Add(i1); t.Add(j0); t.Add(j1);
                }
            }
            int last = 1 + (rings.Count - 1) * seg;
            for (int i = 0; i < seg; i++)
            {
                t.Add(last + i); t.Add(last + (i + 1) % seg); t.Add(tip);
            }

            var mesh = new Mesh { name = "GhostSoul" };
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
