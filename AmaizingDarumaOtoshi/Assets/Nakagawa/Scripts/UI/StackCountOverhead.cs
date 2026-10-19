using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;

namespace Nakagawa
{
    // 試合中、プレイヤーの頭上に積んでいる積み木の数(「3/8」)を出す
    //  ・最大数に近づくほど(m_warnRatio 以上)少しずつ色を変えて少し大きくし、最大数で色を変える(少し脈打つ)
    //  ・数が変わったら弾ませる
    //  ・生きている間だけ出す(幽霊・復活待ち・脱落の間は出さない)
    //  ・名札(MatchDebugView の「1P」など)がある試合では、名札と重ならないようにその上に出す
    //  ・画面の外に出そうなとき(高く積んだときなど)は画面の端に留める
    // DarumaMatch と MatchPlayer を読むだけで、試合には影響しない。MatchHud が試合と同じ GameObject に自動で付ける
    public class StackCountOverhead : MonoBehaviour
    {
        [Header("位置")]
        // 頭(積み木の一番上)からの高さ(m)。名札(MatchDebugView)と同じ基準
        [SerializeField] float m_heightAboveHead = 0.6f;
        // そこからの画面上のずれ(1920x1080 基準のピクセル)。名札があるとき / ないとき
        [SerializeField] float m_offsetWithNameTag = 74.0f;
        [SerializeField] float m_offsetWithoutNameTag = 26.0f;
        [SerializeField] int m_sortingOrder = 21;
        // 画面の外に出そうなときに留める、画面の端からの余白(1920x1080 基準のピクセル)
        [SerializeField, Min(0.0f)] float m_screenMargin = 12.0f;

        [Header("見た目")]
        [SerializeField] float m_fontSize = 34.0f;
        [SerializeField] Color m_normalColor = Color.white;
        // 最大数に近いとき(m_warnRatio 〜 最大数の手前)に近づけていく色
        [SerializeField] Color m_warnColor = new Color(1.0f, 0.78f, 0.22f);
        // 最大数のときの色
        [SerializeField] Color m_maxColor = new Color(1.0f, 0.32f, 0.28f);
        // 最大数に対する割合がこれ以上で色・大きさを変え始める
        [SerializeField, Range(0.0f, 1.0f)] float m_warnRatio = 0.6f;
        // 最大数の手前で一番大きくなる倍率 / 最大数のときの倍率
        [SerializeField, Min(1.0f)] float m_nearMaxScale = 1.2f;
        [SerializeField, Min(1.0f)] float m_maxScale = 1.3f;
        // 最大数のときに脈打つ強さ(0 で脈打たない)
        [SerializeField, Range(0.0f, 0.3f)] float m_maxPulse = 0.06f;
        // 最大数の後ろに小さく「/8」を出す
        [SerializeField] bool m_showMax = true;

        class Tag
        {
            public MatchPlayer Player;
            public TextMeshProUGUI Text;
            public int Last = -1;
            public float Punch;
        }

        DarumaMatch m_match;
        RectTransform m_canvas;
        TMP_FontAsset m_font;
        Material m_mat;
        readonly List<Tag> m_tags = new List<Tag>();

        private void Start()
        {
            m_match = GetComponent<DarumaMatch>();
            if (m_match == null) m_match = DarumaMatch.Current;
            m_font = PresentationSettings.DefaultFont;
            m_mat = ToyKit.PopMaterial(ToyKit.LetterMaterial(m_font));
            m_canvas = ToyKit.CreateOverlayCanvas("StackCountOverhead", m_sortingOrder);
            m_canvas.SetParent(transform, false);
        }

        private void OnDestroy()
        {
            if (m_canvas != null) Destroy(m_canvas.gameObject);
        }

        private void LateUpdate()
        {
            if (m_match == null || m_canvas == null) return;
            SyncTags();
            var cam = Camera.main;
            float offset = HasNameTags() ? m_offsetWithNameTag : m_offsetWithoutNameTag;
            float dt = Time.unscaledDeltaTime;
            foreach (var t in m_tags) UpdateTag(t, cam, offset, dt);
        }

        // 名札(MatchDebugView)を出している試合か
        private bool HasNameTags()
        {
            var view = GetComponent<MatchDebugView>();
            return view != null && view.isActiveAndEnabled;
        }

        private void SyncTags()
        {
            for (int i = m_tags.Count - 1; i >= 0; i--)
            {
                if (m_tags[i].Player != null) continue;
                Destroy(m_tags[i].Text.gameObject);
                m_tags.RemoveAt(i);
            }
            foreach (var player in m_match.Players)
            {
                if (player == null || m_tags.Exists(t => t.Player == player)) continue;
                var text = ToyKit.UIText($"StackCount_{player.Label}", m_canvas, m_font, "", m_fontSize, Color.white);
                text.fontSharedMaterial = m_mat;
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
                ToyKit.Anchor(text.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200.0f, 50.0f));
                m_tags.Add(new Tag { Player = player, Text = text });
            }
        }

        private void UpdateTag(Tag t, Camera cam, float offset, float dt)
        {
            var c = t.Player.Character;
            if (c == null || cam == null || m_match.IsFinished)
            {
                t.Text.gameObject.SetActive(false);
                t.Last = -1;
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(c.transform.position + Vector3.up * (c.Stack.Height + m_heightAboveHead));
            if (sp.z <= 0.0f)
            {
                t.Text.gameObject.SetActive(false);
                return;
            }
            t.Text.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_canvas, sp, null, out var local);
            Vector2 pos = local + new Vector2(0.0f, offset);
            // 高く積んで画面の外に出るときは、画面の端に留める
            Vector2 half = m_canvas.rect.size * 0.5f - new Vector2(m_screenMargin + 40.0f, m_screenMargin + 25.0f);
            pos.x = Mathf.Clamp(pos.x, -half.x, half.x);
            pos.y = Mathf.Clamp(pos.y, -half.y, half.y);
            t.Text.rectTransform.anchoredPosition = pos;

            int count = c.Stack.Count;
            int max = c.Stack.MaxCount;
            if (t.Last >= 0 && t.Last != count) t.Punch = 1.0f;
            t.Last = count;
            t.Punch = Mathf.MoveTowards(t.Punch, 0.0f, dt * 4.0f);

            t.Text.text = m_showMax && max > 0 ? $"{count}<size=60%>/{max}</size>" : count.ToString();

            // 最大数に近いほど色を変えて大きく、最大数で色を変える
            Color color;
            float scale;
            if (max > 0 && count >= max)
            {
                color = m_maxColor;
                scale = m_maxScale * (1.0f + m_maxPulse * Mathf.Sin(Time.unscaledTime * 8.0f));
            }
            else
            {
                float ratio = max > 0 ? (float)count / max : 0.0f;
                float near = m_warnRatio < 1.0f ? Mathf.InverseLerp(m_warnRatio, 1.0f, ratio) : 0.0f;
                color = near > 0.0f ? Color.Lerp(m_normalColor, m_warnColor, near) : m_normalColor;
                scale = Mathf.Lerp(1.0f, m_nearMaxScale, near);
            }
            scale *= 1.0f + 0.3f * Mathf.Sin(t.Punch * Mathf.PI) * t.Punch;
            t.Text.color = color;
            t.Text.rectTransform.localScale = Vector3.one * scale;
        }
    }
}
