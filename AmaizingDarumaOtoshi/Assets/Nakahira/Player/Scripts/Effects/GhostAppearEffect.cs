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

        DarumaGhost m_ghost;
        GhostAppearParams m_params;
        Vector3 m_from;
        Vector3 m_landing;
        Phase m_phase;
        float m_time;
        float m_totalTime;

        GhostSoulVisual m_soul;
        Light m_light;
        MeshRenderer m_markerRing, m_markerDisc;

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

            m_soul = new GhostSoulVisual(transform, m_params);
            BuildMarker();
            if (m_params.LightIntensity > 0.0f)
            {
                var lightGo = new GameObject("MarkerLight");
                lightGo.transform.SetParent(transform, false);
                m_light = lightGo.AddComponent<Light>();
                m_light.type = LightType.Point;
                m_light.shadows = LightShadows.None;
                m_light.intensity = 0.0f;
            }

            m_phase = Phase.Rise;
            m_time = 0.0f;
            m_totalTime = 0.0f;
            m_soul.Move(m_from, true);
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
                m_soul.SetActive(false);
            }
            if (m_phase == Phase.Wait && m_time >= p.WaitTime)
            {
                m_time -= p.WaitTime;
                m_phase = Phase.Fall;
                m_soul.SetActive(true);
                m_soul.Move(m_landing + Vector3.up * p.FallHeight, true);
            }
            if (m_phase == Phase.Fall && m_time >= p.FallTime)
            {
                m_time -= p.FallTime;
                m_phase = Phase.Land;
                m_soul.SetActive(false);
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
            m_soul.Move(m_from + Vector3.up * (p.RiseHeight * p.RiseCurve.Evaluate(t)), false);
            // 出た瞬間はぽんと大きくなり、上の方では消えていく(画面の外なので念のため)
            float pop = EaseOut(m_time / 0.08f);
            float fade = 1.0f - Smooth((t - 0.75f) / 0.25f);
            m_soul.Apply(pop, fade, m_totalTime);
            m_markerRing.enabled = m_markerDisc.enabled = false;
            SetLight(m_landing, 0.0f);
        }

        private void ApplyFall()
        {
            var p = m_params;
            float t = Mathf.Clamp01(m_time / p.FallTime);
            Vector3 top = m_landing + Vector3.up * p.FallHeight;
            Vector3 bottom = m_landing + Vector3.up * p.LandHeight;
            m_soul.Move(Vector3.LerpUnclamped(top, bottom, p.FallCurve.Evaluate(t)), false);
            m_soul.Apply(1.0f, Smooth(t / 0.15f), m_totalTime);
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

        private void SetLight(Vector3 pos, float intensity)
        {
            if (m_light == null) return;
            m_light.transform.position = pos;
            m_light.color = m_params.SoulColor;
            m_light.range = m_params.LightRange;
            m_light.intensity = Mathf.Max(0.0f, intensity);
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
