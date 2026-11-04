using System.Collections.Generic;
using Nakagawa;
using UnityEngine;

namespace Nakahira
{
    // 幽霊が出るときの演出(スプラトゥーンの復活のような流れ)
    //  1. 上がる: やられた所から魂(青白い人魂)が飛び出し、真上へ飛んで画面の外へ消える
    //  2. 待つ　: 着地点の床に目印の輪が出て、だんだん縮んでいく
    //  3. 降りる: 魂が着地点の真上から降ってくる
    //  4. 着地　: 閃光と輪が広がり、幽霊がぽんと出る。ここから幽霊を動かせる
    // 幽霊は先に作っておき、着地するまで隠して止めておく(DarumaGhost.BeginAppear / FinishAppear)
    // 値は幽霊のプレハブに設定した GhostAppearParams。時間はゲーム内の時間(Time.deltaTime)で進む
    public class GhostAppearEffect : MonoBehaviour
    {
        enum Phase { Rise, Wait, Fall, Land }

        // 魂のメッシュのしっぽの長さ(頭の半径を1として)
        const float kTailLength = 2.2f;
        // これ以上の速さでしっぽが一番伸びる(m/秒)
        const float kStretchSpeed = 40.0f;

        DarumaGhost m_ghost;
        GhostAppearParams m_params;
        Vector3 m_from;
        Vector3 m_landing;
        Phase m_phase;
        float m_time;
        float m_totalTime;

        Transform m_soul;
        Transform m_soulBody;
        MeshRenderer m_soulGlow, m_soulCore;
        TrailRenderer m_trail;
        Light m_light;
        MeshRenderer m_markerRing, m_markerDisc;
        Vector3 m_lastSoulPos;
        Vector3 m_soulDir = Vector3.up;

        static Mesh s_soulMesh;

        // やられたキャラクターから魂を出す(落下のときは場外判定の位置から、それ以外はやられた位置の少し上から)
        public static GhostAppearEffect Play(DarumaGhost ghost, DarumaCharacter from)
        {
            if (ghost == null || from == null) return null;
            var p = ghost.AppearParams;
            Vector3 start = from.EliminationCause == EliminationCause.Fall
                ? from.EliminationPosition
                : from.transform.position + Vector3.up * (p != null ? p.StartHeight : 0.0f);
            return Play(ghost, start);
        }

        // soulFrom から魂を出し、幽霊のいる位置に降らせる。幽霊に値が設定されていなければ何もしない(幽霊はすぐ動ける)
        public static GhostAppearEffect Play(DarumaGhost ghost, Vector3 soulFrom)
        {
            if (ghost == null || ghost.AppearParams == null) return null;
            var effect = new GameObject("GhostAppearEffect").AddComponent<GhostAppearEffect>();
            effect.Begin(ghost, soulFrom);
            return effect;
        }

        private void Begin(DarumaGhost ghost, Vector3 soulFrom)
        {
            m_ghost = ghost;
            m_params = ghost.AppearParams;
            m_from = soulFrom;
            m_landing = ghost.transform.position;
            transform.position = m_landing;
            ghost.BeginAppear();
            BuildSoul();
            BuildMarker();

            m_phase = Phase.Rise;
            m_time = 0.0f;
            m_totalTime = 0.0f;
            MoveSoul(m_from, true);
            Apply();
        }

        private void Update()
        {
            if (m_phase != Phase.Land && m_ghost == null)
            {
                // 幽霊が消えた(試合のやり直しなど)
                Destroy(gameObject);
                return;
            }
            if (m_ghost != null) m_landing = m_ghost.transform.position;

            float dt = Time.deltaTime;
            m_time += dt;
            m_totalTime += dt;
            Apply();
        }

        private void OnDestroy()
        {
            // 途中で消えても幽霊が隠れたまま・小さいままにならないようにする
            if (m_ghost == null) return;
            m_ghost.FinishAppear();
            if (m_ghost.Visual != null) m_ghost.Visual.localScale = Vector3.one;
        }

        private void Apply()
        {
            var p = m_params;

            // 時間が余ったら次の段階へ進める(1フレームで複数進むこともある)
            if (m_phase == Phase.Rise && m_time >= p.RiseTime)
            {
                m_time -= p.RiseTime;
                m_phase = Phase.Wait;
                m_soul.gameObject.SetActive(false);
            }
            if (m_phase == Phase.Wait && m_time >= p.WaitTime)
            {
                m_time -= p.WaitTime;
                m_phase = Phase.Fall;
                m_soul.gameObject.SetActive(true);
                MoveSoul(m_landing + Vector3.up * p.FallHeight, true);
            }
            if (m_phase == Phase.Fall && m_time >= p.FallTime)
            {
                m_time -= p.FallTime;
                m_phase = Phase.Land;
                m_soul.gameObject.SetActive(false);
                if (m_ghost != null)
                {
                    m_ghost.FinishAppear();
                    if (m_ghost.Visual != null) m_ghost.Visual.localScale = Vector3.zero;
                }
            }
            if (m_phase == Phase.Land && m_time >= Mathf.Max(p.FlashTime, p.PopTime))
            {
                if (m_ghost != null && m_ghost.Visual != null) m_ghost.Visual.localScale = Vector3.one;
                m_ghost = null;
                Destroy(gameObject);
                return;
            }

            switch (m_phase)
            {
                case Phase.Rise: ApplyRise(); break;
                case Phase.Wait: ApplyMarker(m_time); break;
                case Phase.Fall: ApplyFall(); break;
                case Phase.Land: ApplyLand(); break;
            }
        }

        //========================================
        // 段階ごとの見た目
        //========================================

        private void ApplyRise()
        {
            var p = m_params;
            float t = Mathf.Clamp01(m_time / p.RiseTime);
            MoveSoul(m_from + Vector3.up * (p.RiseHeight * p.RiseCurve.Evaluate(t)), false);
            // 出た瞬間はぽんと大きくなり、上の方では消えていく(画面の外なので念のため)
            float pop = EaseOut(Mathf.Clamp01(m_time / 0.08f));
            float fade = 1.0f - Smooth((t - 0.75f) / 0.25f);
            ApplySoul(pop, fade);
            m_markerRing.enabled = m_markerDisc.enabled = false;
        }

        private void ApplyFall()
        {
            var p = m_params;
            float t = Mathf.Clamp01(m_time / p.FallTime);
            Vector3 top = m_landing + Vector3.up * p.FallHeight;
            Vector3 bottom = m_landing + Vector3.up * p.LandHeight;
            MoveSoul(Vector3.LerpUnclamped(top, bottom, p.FallCurve.Evaluate(t)), false);
            ApplySoul(1.0f, Smooth(t / 0.15f));
            ApplyMarker(p.WaitTime + m_time);
        }

        // 着地点の目印。elapsed は待ち始めてからの時間
        private void ApplyMarker(float elapsed)
        {
            var p = m_params;
            float span = Mathf.Max(0.01f, p.WaitTime + p.FallTime);
            float u = Mathf.Clamp01(elapsed / span);
            float radius = Mathf.Lerp(p.MarkerStartRadius, p.MarkerEndRadius, Smooth(u));
            float pulse = 0.85f + 0.15f * Mathf.Sin(m_totalTime * 14.0f);
            float alpha = p.MarkerAlpha * Smooth(elapsed / 0.2f) * Mathf.Lerp(0.55f, 1.0f, u) * pulse;

            m_markerRing.enabled = m_markerDisc.enabled = true;
            SetFlat(m_markerRing, radius, p.SoulColor, alpha);
            SetFlat(m_markerDisc, radius * 0.9f, p.SoulColor, alpha * 0.35f);
            PlaceMarker();
            SetLight(m_landing + Vector3.up * 0.5f, p.LightIntensity * 0.4f * u * pulse);
        }

        private void ApplyLand()
        {
            var p = m_params;

            // 閃光: 輪が外へ広がり、円が白く光って消える
            float tf = Mathf.Clamp01(m_time / p.FlashTime);
            float fade = 1.0f - tf;
            m_markerRing.enabled = m_markerDisc.enabled = tf < 1.0f;
            SetFlat(m_markerRing, Mathf.Lerp(p.MarkerEndRadius, p.MarkerEndRadius * p.FlashSpread, EaseOut(tf)), p.SoulColor, p.MarkerAlpha * fade);
            SetFlat(m_markerDisc, Mathf.Lerp(p.MarkerEndRadius, p.MarkerEndRadius * 2.0f, tf), Color.white, fade * fade);
            PlaceMarker();
            SetLight(m_landing + Vector3.up * 0.8f, p.LightIntensity * 2.0f * fade);

            // 幽霊がぽんと出る: 0 → 少し大きく → 元の大きさ
            if (m_ghost != null && m_ghost.Visual != null)
            {
                float tp = Mathf.Clamp01(m_time / p.PopTime);
                float scale = tp < 0.6f
                    ? Mathf.Lerp(0.0f, p.PopOvershoot, EaseOut(tp / 0.6f))
                    : Mathf.Lerp(p.PopOvershoot, 1.0f, Smooth((tp - 0.6f) / 0.4f));
                m_ghost.Visual.localScale = Vector3.one * scale;
            }
        }

        //========================================
        // 魂
        //========================================

        // 魂を動かす。jump なら瞬間移動(軌跡を消す)
        private void MoveSoul(Vector3 pos, bool jump)
        {
            if (!jump)
            {
                Vector3 delta = pos - m_lastSoulPos;
                if (delta.sqrMagnitude > 0.000001f) m_soulDir = delta.normalized;
                float dt = Time.deltaTime;
                float speed = dt > 0.0f ? delta.magnitude / dt : 0.0f;
                m_soulStretch = Mathf.Lerp(1.0f, m_params.TailStretch, Mathf.Clamp01(speed / kStretchSpeed));
            }
            m_soul.position = pos;
            m_lastSoulPos = pos;
            if (jump) m_trail.Clear();
        }

        float m_soulStretch = 1.0f;

        // size: 大きさの倍率 / alpha: 濃さの倍率
        private void ApplySoul(float size, float alpha)
        {
            var p = m_params;
            float flicker = Mathf.Sin(m_totalTime * p.FlickerSpeed);
            float r = p.SoulRadius * size * (1.0f + 0.08f * flicker);

            // しっぽは進む向きの反対へ。炎のようにゆらゆら揺らす
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, -m_soulDir);
            Quaternion sway = Quaternion.Euler(
                Mathf.Sin(m_totalTime * p.FlickerSpeed * 0.7f) * 10.0f, 0.0f,
                Mathf.Cos(m_totalTime * p.FlickerSpeed * 0.9f) * 10.0f);
            m_soulBody.rotation = rot * sway;
            m_soulBody.localScale = new Vector3(r, r * p.TailLength * m_soulStretch * (1.0f - 0.06f * flicker), r);

            Color glow = p.SoulColor;
            glow.a = 0.45f * alpha;
            EffectMeshes.SetColor(m_soulGlow, glow);
            Color core = p.CoreColor;
            core.a = alpha;
            EffectMeshes.SetColor(m_soulCore, core);

            m_trail.time = p.TrailTime;
            m_trail.widthMultiplier = p.TrailWidth * size;
            Color trailColor = p.SoulColor;
            m_trail.colorGradient = TrailGradient(trailColor, 0.7f * alpha);

            if (m_light != null)
            {
                m_light.transform.position = m_soul.position;
                m_light.color = p.SoulColor;
                m_light.range = p.LightRange;
                m_light.intensity = p.LightIntensity * alpha;
            }
        }

        private void BuildSoul()
        {
            var p = m_params;
            m_soul = new GameObject("Soul").transform;
            m_soul.SetParent(transform, false);
            m_soulBody = new GameObject("Body").transform;
            m_soulBody.SetParent(m_soul, false);

            // 外側の光(大きめ・薄め)と芯。芯を手前に描く
            m_soulGlow = EffectMeshes.Create("Glow", m_soulBody, SoulMesh, false);
            m_soulGlow.transform.localScale = Vector3.one * 1.6f;
            m_soulGlow.sortingOrder = 0;
            m_soulCore = EffectMeshes.Create("Core", m_soulBody, SoulMesh, false);
            m_soulCore.sortingOrder = 1;

            m_trail = m_soul.gameObject.AddComponent<TrailRenderer>();
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
                lightGo.transform.SetParent(transform, false);
                m_light = lightGo.AddComponent<Light>();
                m_light.type = LightType.Point;
                m_light.shadows = LightShadows.None;
                m_light.color = p.SoulColor;
                m_light.range = p.LightRange;
                m_light.intensity = 0.0f;
            }
        }

        private void SetLight(Vector3 pos, float intensity)
        {
            if (m_light == null) return;
            m_light.transform.position = pos;
            m_light.color = m_params.SoulColor;
            m_light.range = m_params.LightRange;
            m_light.intensity = Mathf.Max(0.0f, intensity);
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
        // 目印
        //========================================

        private void BuildMarker()
        {
            m_markerDisc = EffectMeshes.Create("MarkerDisc", transform, EffectMeshes.Disc, false);
            m_markerRing = EffectMeshes.Create("MarkerRing", transform, EffectMeshes.Ring, false);
            m_markerDisc.enabled = m_markerRing.enabled = false;
        }

        // 床とちらつかないよう少し浮かせる
        private void PlaceMarker()
        {
            m_markerDisc.transform.position = m_landing + Vector3.up * 0.02f;
            m_markerRing.transform.position = m_landing + Vector3.up * 0.03f;
        }

        private static void SetFlat(Renderer r, float radius, Color c, float alpha)
        {
            r.transform.localScale = new Vector3(radius, 1.0f, radius);
            c.a = Mathf.Clamp01(alpha);
            EffectMeshes.SetColor(r, c);
        }

        //========================================
        // 人魂のメッシュ(丸い頭 + 先が細く透明になるしっぽ。しっぽは +Y 向き)
        //========================================

        static Mesh SoulMesh => s_soulMesh != null ? s_soulMesh : s_soulMesh = BuildSoulMesh();

        static Mesh BuildSoulMesh()
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

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1.0f - (1.0f - t) * (1.0f - t);
        }
    }
}
