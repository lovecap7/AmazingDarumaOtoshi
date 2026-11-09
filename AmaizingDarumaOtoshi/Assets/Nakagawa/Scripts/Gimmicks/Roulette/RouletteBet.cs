using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nakagawa
{
    // ディーラー側の賭け(Stage20)。プレイヤーは賭けない。盤の外の賭けの表に、回すたびに色(赤・黒・緑の0)を1つ選んでチップを置く
    //  ・置くとき: チップの山が空から落ちてきて、選んだ色のマスが光る
    //  ・画面の右上に小さな窓を出し、賭けの表の上に置いたサブカメラの映像で「どこに賭けたか」を見せる
    //  ・球が止まったら判定: 的中なら配当として盤全体に積み木が降る(0 は多め)、ハズレならチップは場に没収される
    // RouletteGame が Place → Resolve → Clear の順に呼ぶ
    public class RouletteBet : MonoBehaviour
    {
        [Header("賭けの表")]
        [Tooltip("賭けるマスの中心(緑・赤・黒の順)")]
        [SerializeField] Transform[] m_spots;
        [Tooltip("マスの大きさ X・Z(光る枠の大きさ)")]
        [SerializeField] Vector2 m_spotSize = new Vector2(4.2f, 3.4f);
        [Tooltip("色を選ぶ重み(緑・赤・黒の順)")]
        [SerializeField] float[] m_weights = { 0.12f, 0.44f, 0.44f };
        [Tooltip("チップの山(無効にした見本を複製する)")]
        [SerializeField] GameObject m_chipTemplate;
        [SerializeField] float m_dropHeight = 8.0f;
        [SerializeField] float m_dropTime = 0.45f;

        [Header("配当")]
        [SerializeField] RouletteTumikiRain m_payoutRain;
        [SerializeField, Min(0)] int m_payoutCount = 10;
        [SerializeField, Min(0)] int m_zeroPayoutCount = 18;

        [Header("画面の窓")]
        [SerializeField] Camera m_camera;
        [SerializeField] Vector2Int m_textureSize = new Vector2Int(640, 400);
        [Tooltip("窓の大きさ(UIの単位。1920×1080基準)")]
        [SerializeField] Vector2 m_viewSize = new Vector2(440.0f, 275.0f);
        [SerializeField] Vector2 m_viewMargin = new Vector2(36.0f, 150.0f);
        [SerializeField] int m_sortingOrder = 16;
        [SerializeField] string m_title = "ディーラーのベット";
        [SerializeField] string m_hitText = "的中! 配当の積み木!";
        [SerializeField] string m_missText = "ハズレ… チップ没収";
        [SerializeField] Color m_red = new Color(0.95f, 0.25f, 0.25f, 1.0f);
        [SerializeField] Color m_black = new Color(0.75f, 0.75f, 0.85f, 1.0f);
        [SerializeField] Color m_green = new Color(0.3f, 0.95f, 0.45f, 1.0f);
        [SerializeField] Color m_gold = new Color(1.0f, 0.85f, 0.3f, 1.0f);

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_placeEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.9f, 0.5f, 1.0f), ColorB = Color.white,
            Count = 18, Speed = new Vector2(1.5f, 4.0f), Size = new Vector2(0.06f, 0.14f), Lifetime = new Vector2(0.3f, 0.6f),
            Gravity = 0.3f, Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.6f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.9f, 0.5f, 0.8f), ShockwaveRadius = 2.4f, ShockwaveTime = 0.3f,
        };
        [SerializeField] GimmickBurstSettings m_hitEffect = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.85f, 0.25f, 1.0f), ColorB = Color.white,
            Count = 40, Speed = new Vector2(3.0f, 7.0f), Size = new Vector2(0.08f, 0.18f), Lifetime = new Vector2(0.5f, 1.0f),
            Gravity = 0.4f, Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.8f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.85f, 0.3f, 0.9f), ShockwaveRadius = 3.5f, ShockwaveTime = 0.4f,
        };

        enum State { None, Dropping, Placed, Won, Lost }

        State m_state;
        float m_time;
        int m_kind = -1;
        Transform m_chip;
        Vector3 m_chipScale = Vector3.one;
        MeshRenderer m_glow;
        Mesh m_glowMesh;

        RectTransform m_canvas;
        CanvasGroup m_group;
        RawImage m_view;
        TextMeshProUGUI m_label;
        TextMeshProUGUI m_result;
        RenderTexture m_texture;
        float m_viewAlpha;
        bool m_showView;

        // 今の賭け(なければ null)
        public RouletteWheel.PocketKind? Current => m_kind >= 0 ? (RouletteWheel.PocketKind?)m_kind : null;

        private void OnDisable()
        {
            Clear();
            m_viewAlpha = 0.0f;
            ApplyView();
        }

        private void OnDestroy()
        {
            if (m_texture != null) m_texture.Release();
            if (m_canvas != null) Destroy(m_canvas.gameObject);
        }

        // 重みに従って色を選んで賭ける
        public RouletteWheel.PocketKind PlaceRandom()
        {
            float sum = 0.0f;
            foreach (float w in m_weights) sum += Mathf.Max(0.0f, w);
            float r = Random.value * sum;
            int kind = 0;
            for (int i = 0; i < m_weights.Length; i++)
            {
                r -= Mathf.Max(0.0f, m_weights[i]);
                if (r <= 0.0f) { kind = i; break; }
            }
            var k = (RouletteWheel.PocketKind)Mathf.Clamp(kind, 0, 2);
            Place(k);
            return k;
        }

        public void Place(RouletteWheel.PocketKind kind)
        {
            Clear();
            m_kind = (int)kind;
            Transform spot = Spot(m_kind);
            if (spot == null || m_chipTemplate == null) return;

            var chip = Instantiate(m_chipTemplate, transform);
            chip.SetActive(true);
            m_chip = chip.transform;
            m_chipScale = m_chip.localScale;
            m_chip.position = spot.position + Vector3.up * m_dropHeight;

            if (m_glowMesh == null) m_glowMesh = GimmickMeshes.BuildQuad();
            m_glow = EffectMeshes.Create("BetGlow", transform, m_glowMesh, true);
            m_glow.transform.position = spot.position + Vector3.up * 0.05f;
            m_glow.transform.rotation = spot.rotation;
            m_glow.transform.localScale = new Vector3(m_spotSize.x, 1.0f, m_spotSize.y);

            m_state = State.Dropping;
            m_time = 0.0f;
            ShowView(true);
            m_label.text = $"{KindName(m_kind)} にベット!";
            m_label.color = KindColor(m_kind);
            m_result.text = "";
            GimmickSound.Play(GimmickSound.Launch, 0.25f, 1.4f);
        }

        // 球が止まった色で判定する。的中なら true
        public bool Resolve(RouletteWheel.PocketKind result, RouletteWheel wheel)
        {
            if (m_kind < 0) return false;
            bool hit = (int)result == m_kind;
            m_state = hit ? State.Won : State.Lost;
            m_time = 0.0f;
            Transform spot = Spot(m_kind);
            if (hit)
            {
                m_result.text = m_hitText;
                m_result.color = m_gold;
                if (spot != null) GimmickBurst.Play(m_hitEffect, spot.position + Vector3.up * 0.3f);
                GimmickSound.Play(GimmickSound.Charge, 0.35f, 1.8f);
                if (m_payoutRain != null && wheel != null)
                {
                    int count = result == RouletteWheel.PocketKind.Green ? m_zeroPayoutCount : m_payoutCount;
                    m_payoutRain.Rain(wheel, 0.0f, 180.0f, count);
                }
            }
            else
            {
                m_result.text = m_missText;
                m_result.color = new Color(0.7f, 0.7f, 0.75f, 1.0f);
                GimmickSound.Play(GimmickSound.Pop, 0.3f, 0.6f);
            }
            return hit;
        }

        // チップと光る枠を片付け、窓を閉じる
        public void Clear()
        {
            if (m_chip != null) Destroy(m_chip.gameObject);
            if (m_glow != null) Destroy(m_glow.gameObject);
            m_chip = null;
            m_glow = null;
            m_kind = -1;
            m_state = State.None;
            ShowView(false);
        }

        private void Update()
        {
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            m_time += dt;
            Transform spot = m_kind >= 0 ? Spot(m_kind) : null;

            if (m_chip != null && spot != null)
            {
                switch (m_state)
                {
                    case State.Dropping:
                    {
                        float t = Mathf.Clamp01(m_time / m_dropTime);
                        m_chip.position = spot.position + Vector3.up * (m_dropHeight * (1.0f - t * t));
                        if (t >= 1.0f)
                        {
                            m_state = State.Placed;
                            m_time = 0.0f;
                            GimmickBurst.Play(m_placeEffect, spot.position + Vector3.up * 0.1f);
                            GimmickSound.Play(BilliardFx.Clack, 0.45f, 1.7f);
                            GimmickSound.Play(BilliardFx.Clack, 0.3f, 2.0f);
                        }
                        break;
                    }
                    case State.Placed:
                    {
                        // 着地の弾み
                        float b = Mathf.Max(0.0f, Mathf.Sin(m_time * 18.0f)) * Mathf.Exp(-m_time * 8.0f);
                        m_chip.position = spot.position + Vector3.up * (0.35f * b);
                        break;
                    }
                    case State.Won:
                    {
                        // 配当で山が高くなる
                        float s = Mathf.Lerp(1.0f, 2.2f, Mathf.Clamp01(m_time / 0.5f));
                        m_chip.localScale = new Vector3(m_chipScale.x, m_chipScale.y * s, m_chipScale.z);
                        break;
                    }
                    case State.Lost:
                    {
                        // 場の外へ回収される
                        float t = Mathf.Clamp01(m_time / 0.8f);
                        m_chip.position = spot.position + spot.forward * (6.0f * t * t) + Vector3.up * (1.5f * Mathf.Sin(t * Mathf.PI));
                        m_chip.localScale = m_chipScale * (1.0f - t * 0.8f);
                        break;
                    }
                }
            }

            if (m_glow != null)
            {
                Color c = m_state == State.Won ? m_gold : m_state == State.Lost ? new Color(0.4f, 0.4f, 0.45f, 1.0f) : KindColor(m_kind);
                float blink = GimmickPalette.Blink(Time.time, m_state == State.Won ? 8.0f : 2.5f);
                EffectMeshes.SetColor(m_glow, GimmickPalette.WithAlpha(c, Mathf.Lerp(0.25f, 0.6f, blink)));
            }

            // 窓のフェード
            float target = m_showView ? 1.0f : 0.0f;
            m_viewAlpha = Mathf.MoveTowards(m_viewAlpha, target, Time.unscaledDeltaTime * 4.0f);
            ApplyView();
        }

        //========================================
        // 画面の窓(サブカメラの映像)
        //========================================

        void ShowView(bool show)
        {
            if (show) EnsureView();
            m_showView = show;
        }

        void ApplyView()
        {
            if (m_group != null)
            {
                m_group.alpha = m_viewAlpha;
                // 右から滑り込む
                var r = (RectTransform)m_group.transform;
                r.anchoredPosition = new Vector2(-m_viewMargin.x + (1.0f - m_viewAlpha) * 120.0f, -m_viewMargin.y);
            }
            if (m_camera != null) m_camera.enabled = m_viewAlpha > 0.0f;
        }

        void EnsureView()
        {
            if (m_canvas != null) return;
            if (m_camera != null)
            {
                m_texture = new RenderTexture(m_textureSize.x, m_textureSize.y, 24) { name = "RouletteBetView", antiAliasing = 2 };
                m_camera.targetTexture = m_texture;
                m_camera.enabled = false;
            }
            var font = PresentationSettings.DefaultFont;
            m_canvas = ToyKit.CreateOverlayCanvas("RouletteBetView", m_sortingOrder);
            var panel = ToyKit.UIRounded("Panel", m_canvas, new Color(0.06f, 0.05f, 0.08f, 0.85f), 24.0f);
            ToyKit.Anchor(panel, new Vector2(1.0f, 1.0f), new Vector2(-m_viewMargin.x, -m_viewMargin.y), m_viewSize + new Vector2(24.0f, 110.0f));
            panel.pivot = new Vector2(1.0f, 1.0f);
            m_group = panel.gameObject.AddComponent<CanvasGroup>();
            m_group.blocksRaycasts = false;

            var title = ToyKit.UIText("Title", panel, font, m_title, 30.0f, new Color(1.0f, 0.9f, 0.6f, 1.0f));
            ToyKit.Anchor(title.rectTransform, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -26.0f), new Vector2(m_viewSize.x, 40.0f));

            var frame = ToyKit.UIRounded("Frame", panel, new Color(0.95f, 0.75f, 0.3f, 1.0f), 10.0f);
            ToyKit.Anchor(frame, new Vector2(0.5f, 1.0f), new Vector2(0.0f, -52.0f - m_viewSize.y * 0.5f), m_viewSize + new Vector2(8.0f, 8.0f));
            var viewGo = new GameObject("View", typeof(RectTransform));
            viewGo.transform.SetParent(frame, false);
            m_view = viewGo.AddComponent<RawImage>();
            m_view.texture = m_texture;
            m_view.raycastTarget = false;
            ToyKit.Anchor(m_view.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, m_viewSize);

            m_label = ToyKit.UIText("Label", panel, font, "", 34.0f, Color.white);
            ToyKit.Anchor(m_label.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 38.0f), new Vector2(m_viewSize.x, 40.0f));
            m_result = ToyKit.UIText("Result", frame, font, "", 40.0f, m_gold);
            ToyKit.Anchor(m_result.rectTransform, new Vector2(0.5f, 0.0f), new Vector2(0.0f, 34.0f), new Vector2(m_viewSize.x, 50.0f));
            var mat = font != null ? ToyKit.LetterMaterial(font) : null;
            if (mat != null)
            {
                title.fontSharedMaterial = mat;
                m_label.fontSharedMaterial = mat;
                m_result.fontSharedMaterial = mat;
            }
            m_group.alpha = 0.0f;
        }

        Transform Spot(int kind) => m_spots != null && kind >= 0 && kind < m_spots.Length ? m_spots[kind] : null;

        string KindName(int kind) => kind == 0 ? "緑の0" : kind == 1 ? "赤" : "黒";
        Color KindColor(int kind) => kind == 0 ? m_green : kind == 1 ? m_red : m_black;
    }
}
