using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Yurube.Achievements
{
    // 試合後のリザルトで「今回解除した実績」を1枚ずつ見せる係（表示だけ）。
    //  ・解除の記録・保存は AchievementManager（データ側）が持つ。ここは UI と演出だけ。
    //  ・画面の右からカードがスライドイン → 少し見せる → 次のカード
    //  ・A / Enter / クリックで次へ、B / Esc で残りをスキップ（MenuInput 経由）
    //  ・多くても ResultMaxCards 枚まで個別に見せ、残りは「ほか○件」にまとめる
    //  ・最後に「実績 37/39 → 39/39」のゲージを伸ばす
    //  ・1件も無ければ何も出さずにすぐ終わる（リザルトのテンポを落とさない）
    // レアリティごとの見せ方は AchievementRevealStyle（ScriptableObject）で差し替えられる。
    public class AchievementResultPresenter : MonoBehaviour
    {
        // 右下の操作説明（リザルト画面の「A：つぎへ」と同じ位置・大きさ）
        const string HintText = "A：つぎへ　B：スキップ";

        // カードの大きさと、右端からの位置（リザルト画面の UI と同じ 1920×1080 の座標）
        const float CardW = 820f, CardH = 520f;
        const float RestX = -70f, RestY = 40f;
        const float HiddenX = 900f;      // 画面の右の外（pivot は右端）
        const float ShineW = 220f, ShineAngle = 28f;

        // 見た目は「じっせき」の詳細カードとそろえる
        static readonly Color CardColor = new Color(1f, 0.98f, 0.94f, 0.97f);
        static readonly Color AccentColor = ToyKit.Palette[4];
        static readonly Color Ink = ToyKit.Ink;
        static readonly Color GaugeBack = new Color(ToyKit.Ink.r, ToyKit.Ink.g, ToyKit.Ink.b, 0.16f);

        public bool IsPlaying { get; private set; }

        TMP_FontAsset m_font;
        TMP_FontAsset m_bodyFont;
        // フォントの既定マテリアルは白い縁取り付き。カードの文字では縁を消す
        Material m_plainMat;
        AudioSource m_audio;

        RectTransform m_root, m_card;
        CanvasGroup m_cardGroup, m_progressGroup;
        Image m_dim, m_flash, m_edge, m_border, m_fill, m_shine, m_icon;
        RectTransform m_shineRect;
        TextMeshProUGUI m_name, m_date, m_condition, m_poem, m_hint, m_progressLabel;
        Image m_gaugeFill;
        bool m_skipped;

        AchievementSettings Settings => AchievementSettings.Instance;

        float SlideSeconds => Settings != null ? Settings.ResultSlideSeconds : 0.45f;
        float HoldSeconds => Settings != null ? Settings.ResultHoldSeconds : 2.2f;
        float ProgressSeconds => Settings != null ? Settings.ResultProgressSeconds : 1.8f;
        int MaxCards => Settings != null ? Settings.ResultMaxCards : 3;

        static TMP_FontAsset s_bodyFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBodyFont() => s_bodyFont = null;

        TMP_FontAsset BodyFont => m_bodyFont != null ? m_bodyFont : m_font;

        //========================================
        // 作る・呼ぶ
        //========================================

        // リザルト画面の UI（uiRoot）の下に作る
        public static AchievementResultPresenter Create(RectTransform parent, TMP_FontAsset font)
        {
            var go = new GameObject("AchievementResult", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ToyKit.Stretch((RectTransform)go.transform);
            var presenter = go.AddComponent<AchievementResultPresenter>();
            presenter.m_font = font;
            return presenter;
        }

        // 効果音（試合と同じく、メニューのサウンド設定に従う）
        void PlaySound(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            if (m_audio == null)
            {
                m_audio = gameObject.AddComponent<AudioSource>();
                m_audio.playOnAwake = false;
                m_audio.spatialBlend = 0f;
            }
            var sounds = MatchSounds.Instance;
            m_audio.pitch = pitch;
            m_audio.volume = volume * (sounds != null ? sounds.SeVolume : 1f) * GameSettings.SeVolume;
            m_audio.PlayOneShot(clip);
        }

        // リザルトの流れに挟む（0件ならすぐ戻る）
        public IEnumerator Play()
        {
            if (IsPlaying) yield break;
            var pending = AchievementManager.PendingAchievements();
            if (pending.Count == 0) yield break;

            IsPlaying = true;
            Build();
            try
            {
                yield return PlayRoutine(pending);
            }
            finally
            {
                IsPlaying = false;
                HideEverything();
                // 見せ終わった印は、演出が終わってからつける（途中で落ちたら次回また出せる）
                AchievementManager.MarkNotified(pending);
            }
        }

        IEnumerator PlayRoutine(List<AchievementData> pending)
        {
            m_skipped = false;
            m_dim.color = new Color(m_dim.color.r, m_dim.color.g, m_dim.color.b, 0f);
            m_hint.alpha = 0f;

            // 背景を少し暗くして、カードに目を向けさせる
            yield return FadeDim(0f, 0.45f, 0.25f);
            m_hint.alpha = 1f;

            int max = Mathf.Max(1, MaxCards);
            int shown = 0;
            for (int i = 0; i < pending.Count && shown < max; i++)
            {
                var style = StyleFor(pending[i]);
                yield return ShowCard(pending[i], style);
                shown++;
                if (m_skipped) break;
            }

            if (!m_skipped && pending.Count > shown)
            {
                yield return ShowMore(pending.Count - shown, pending, shown);
            }

            if (!m_skipped)
            {
                m_cardGroup.alpha = 0f;
                yield return ShowProgress(pending.Count);
            }

            m_hint.alpha = 0f;
            yield return FadeDim(m_dim.color.a, 0f, 0.25f);
            m_progressGroup.alpha = 0f;
            m_cardGroup.alpha = 0f;
        }

        AchievementRevealStyle StyleFor(AchievementData data)
        {
            var settings = Settings;
            if (settings != null) return settings.StyleFor(data.Rarity);
            return AchievementRevealStyle.GetDefault(data.Rarity);
        }

        //========================================
        // カード1枚
        //========================================

        IEnumerator ShowCard(AchievementData data, AchievementRevealStyle style)
        {
            ApplyCard(data);

            m_cardGroup.alpha = 1f;
            m_card.anchoredPosition = new Vector2(HiddenX, RestY);
            m_card.localScale = Vector3.one;

            // 音と、レアリティごとの追加演出
            if (style.Sound != null) PlaySound(style.Sound, style.Volume, style.Pitch);
            if (style.Flash) StartCoroutine(FlashSpike());
            if (style.Sparkles > 0) StartCoroutine(SparkleBurst(style.Sparkles, data.DisplayRarityColor));

            yield return SlideIn(style.SlideSeconds > 0f ? style.SlideSeconds : SlideSeconds, style.Punch);

            // 光の帯を、指定の回数だけ横切らせる
            if (style.ShineCount > 0) StartCoroutine(ShineSweeps(style.ShineCount, style.ShineSeconds));
            else StartCoroutine(ShineSweeps(1, 0.7f));   // 最低1回は光を通す（軽い演出）

            yield return WaitForNext(style.HoldSeconds > 0f ? style.HoldSeconds : HoldSeconds);

            yield return HideCard();
        }

        // 「ほか○件」のまとめカード
        IEnumerator ShowMore(int count, List<AchievementData> pending, int from)
        {
            m_name.text = "ほか " + count + " 件";
            m_date.text = "";
            m_condition.text = NamesOf(pending, from);
            m_poem.text = "リザルトのあと「じっせき」で見られます。";
            m_icon.enabled = false;
            m_fill.sprite = AchievementVisuals.CardGradient(AchievementRarity.Bronze, true);
            m_border.color = AchievementRarityInfo.BorderOf(AchievementRarity.Bronze, true);
            m_edge.color = AchievementRarityInfo.EdgeOf(AchievementRarity.Bronze, true);

            m_cardGroup.alpha = 1f;
            m_card.anchoredPosition = new Vector2(HiddenX, RestY);
            m_card.localScale = Vector3.one;
            PlaySound(SoundSynth.Pickup, 0.7f, 0.9f);
            yield return SlideIn(SlideSeconds, 1.06f);
            yield return WaitForNext(HoldSeconds + 0.6f);
            yield return HideCard();
        }

        string NamesOf(List<AchievementData> pending, int from)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = from; i < pending.Count; i++)
            {
                if (sb.Length > 0) sb.Append("、");
                sb.Append(pending[i].Name);
                if (sb.Length > 120) { sb.Append(" ほか"); break; }
            }
            return sb.ToString();
        }

        void ApplyCard(AchievementData data)
        {
            m_name.text = data.Name;
            m_date.text = data.UnlockedTextSeconds;
            m_condition.text = data.ConditionText;
            m_poem.text = data.Poem;

            Sprite icon = AchievementRarityIcons.IconOf(data.Rarity, data.IsSpecial);
            m_icon.sprite = icon;
            m_icon.color = Color.white;
            m_icon.enabled = icon != null;

            m_fill.sprite = AchievementVisuals.CardGradient(data.Rarity, true);
            m_border.color = data.IsSpecial ? AchievementRarityInfo.SpecialColor : AchievementRarityInfo.BorderOf(data.Rarity, true);
            m_edge.color = data.IsSpecial
                ? Color.Lerp(AchievementRarityInfo.SpecialColor, ToyKit.Ink, 0.45f)
                : AchievementRarityInfo.EdgeOf(data.Rarity, true);
            m_shine.color = new Color(1f, 1f, 1f, 0f);
        }

        //========================================
        // 動き
        //========================================

        IEnumerator SlideIn(float time, float punch)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, time))
            {
                float u = Mathf.Clamp01(t);
                // 行き過ぎてから戻る（EaseOutBack）と、少しだけ大きく出る
                float e = ToyKit.EaseOutBack(u);
                m_card.anchoredPosition = new Vector2(Mathf.Lerp(HiddenX, RestX, e), RestY);
                m_card.localScale = Vector3.one * Mathf.Lerp(1f, punch, Mathf.Sin(u * Mathf.PI) * (1f - u));
                yield return null;
            }
            m_card.anchoredPosition = new Vector2(RestX, RestY);
            m_card.localScale = Vector3.one;
        }

        IEnumerator HideCard()
        {
            Vector2 from = m_card.anchoredPosition;
            float scale = m_card.localScale.x;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.18f)
            {
                float u = Mathf.Clamp01(t);
                m_card.anchoredPosition = from + new Vector2(0f, 30f * u);
                m_card.localScale = Vector3.one * Mathf.Lerp(scale, 0.96f, u);
                m_cardGroup.alpha = 1f - u;
                yield return null;
            }
            m_cardGroup.alpha = 0f;
        }

        // 入力待ち。A で次へ、B で残りをスキップ
        IEnumerator WaitForNext(float hold)
        {
            const float lockTime = 0.15f;   // 直前の「早送り」の押下を拾わないように
            float t = 0f;
            while (t < hold)
            {
                if (t > lockTime)
                {
                    if (MenuInput.Cancel()) { m_skipped = true; yield break; }
                    if (MenuInput.Submit() || MenuInput.AnyClick()) yield break;
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator ShineSweeps(int count, float seconds)
        {
            float time = Mathf.Max(0.15f, seconds);
            for (int i = 0; i < count; i++)
            {
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / time)
                {
                    float u = Mathf.Clamp01(t);
                    float x = Mathf.Lerp(-CardW - ShineW, CardW + ShineW, u);
                    m_shineRect.anchoredPosition = new Vector2(x, 0f);
                    m_shine.color = new Color(1f, 1f, 1f, 0.30f * Mathf.Sin(u * Mathf.PI));
                    yield return null;
                }
                m_shine.color = new Color(1f, 1f, 1f, 0f);
                yield return null;
            }
        }

        IEnumerator FlashSpike()
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f)
            {
                float u = Mathf.Clamp01(t);
                m_flash.color = new Color(1f, 1f, 1f, 0.55f * (1f - u) * (1f - u));
                yield return null;
            }
            m_flash.color = new Color(1f, 1f, 1f, 0f);
        }

        // カードのまわりにきらめきを散らす（上位レアリティ用）
        IEnumerator SparkleBurst(int count, Color color)
        {
            var items = new List<RectTransform>(count);
            var dirs = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                var rt = ToyKit.UIImage("Sparkle", m_root, new Color(color.r, color.g, color.b, 0f), AchievementVisuals.Sparkle());
                var img = rt.GetComponent<Image>();
                img.type = Image.Type.Simple;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.sizeDelta = Vector2.one * UnityEngine.Random.Range(24f, 56f);
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 start = new Vector2(RestX - CardW * 0.5f, RestY)
                               + new Vector2(UnityEngine.Random.Range(-CardW * 0.55f, CardW * 0.55f),
                                             UnityEngine.Random.Range(-CardH * 0.55f, CardH * 0.55f));
                rt.anchoredPosition = start;
                items.Add(rt);
                dirs.Add(dir);
            }

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.7f)
            {
                float u = Mathf.Clamp01(t);
                float alpha = Mathf.Sin(u * Mathf.PI) * 0.95f;
                for (int i = 0; i < items.Count; i++)
                {
                    var rt = items[i];
                    rt.anchoredPosition += dirs[i] * (140f * Time.unscaledDeltaTime);
                    rt.localRotation = Quaternion.Euler(0f, 0f, 90f * u * (i % 2 == 0 ? 1f : -1f));
                    rt.localScale = Vector3.one * (0.6f + 0.8f * u);
                    var img = rt.GetComponent<Image>();
                    img.color = new Color(color.r, color.g, color.b, alpha);
                }
                yield return null;
            }

            foreach (var rt in items) if (rt != null) Destroy(rt.gameObject);
        }

        //========================================
        // 進捗（実績 37/39 → 39/39）
        //========================================

        IEnumerator ShowProgress(int shownCount)
        {
            int total = Mathf.Max(1, AchievementManager.All.Count);
            int to = AchievementManager.UnlockedCount;
            int from = Mathf.Clamp(to - shownCount, 0, to);
            // ゲージと一緒に、右側の数も from から to まで増えていく
            SetProgressText(from, from, total);

            m_progressGroup.alpha = 0f;
            SetGauge((float)from / total);
            yield return FadeGroup(m_progressGroup, 0f, 1f, 0.2f);

            int last = from;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.2f, ProgressSeconds))
            {
                float u = Mathf.Clamp01(t);
                float eased = Mathf.SmoothStep(0f, 1f, u);
                SetGauge(Mathf.Lerp((float)from / total, (float)to / total, eased));
                // 数字は1つずつ増える（増えた瞬間に少しだけ弾ませる）
                int count = Mathf.RoundToInt(Mathf.Lerp(from, to, eased));
                if (count != last)
                {
                    last = count;
                    SetProgressText(from, count, total);
                    m_progressLabel.rectTransform.localScale = Vector3.one * 1.06f;
                }
                m_progressLabel.rectTransform.localScale = Vector3.MoveTowards(
                    m_progressLabel.rectTransform.localScale, Vector3.one, Time.unscaledDeltaTime * 0.6f);
                yield return null;
            }
            SetGauge((float)to / total);
            SetProgressText(from, to, total);
            m_progressLabel.rectTransform.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(0.9f);
        }

        void SetProgressText(int from, int current, int total)
        {
            m_progressLabel.text = "実績 " + from + "/" + total + " → " + current + "/" + total;
        }

        void SetGauge(float ratio)
        {
            var rt = m_gaugeFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        IEnumerator FadeDim(float from, float to, float time)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, time))
            {
                float a = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                m_dim.color = new Color(m_dim.color.r, m_dim.color.g, m_dim.color.b, a);
                yield return null;
            }
            m_dim.color = new Color(m_dim.color.r, m_dim.color.g, m_dim.color.b, to);
        }

        IEnumerator FadeGroup(CanvasGroup group, float from, float to, float time)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, time))
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }
            group.alpha = to;
        }

        void HideEverything()
        {
            if (m_root == null) return;
            m_dim.color = new Color(m_dim.color.r, m_dim.color.g, m_dim.color.b, 0f);
            m_flash.color = new Color(1f, 1f, 1f, 0f);
            m_cardGroup.alpha = 0f;
            m_progressGroup.alpha = 0f;
            m_hint.alpha = 0f;
            m_shine.color = new Color(1f, 1f, 1f, 0f);
        }

        //========================================
        // UI の組み立て（じっせきの詳細カードと同じ見た目）
        //========================================

        void Build()
        {
            if (m_root != null) return;

            var root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(transform, false);
            ToyKit.Stretch(root);
            m_root = root;

            // 文字の白い縁取りを消したマテリアル（フォントの既定は縁取り付き）
            if (m_font != null && m_font.material != null)
            {
                m_plainMat = new Material(m_font.material);
                m_plainMat.SetFloat("_OutlineWidth", 0f);
                m_plainMat.DisableKeyword("OUTLINE_ON");
            }
            if (s_bodyFont == null)
            {
                var source = Resources.Load<Font>("AchievementBodyFont");
                if (source != null) s_bodyFont = TMP_FontAsset.CreateFontAsset(source);
            }
            m_bodyFont = s_bodyFont;

            // 背景を暗くする膜
            var dim = ToyKit.UIImage("Dim", root, new Color(0.04f, 0.02f, 0.01f, 0f));
            ToyKit.Stretch(dim);
            m_dim = dim.GetComponent<Image>();

            BuildCard(root);
            BuildProgress(root);

            // 白いフラッシュ（上位レアリティ）
            var flash = ToyKit.UIImage("Flash", root, new Color(1f, 1f, 1f, 0f));
            ToyKit.Stretch(flash);
            m_flash = flash.GetComponent<Image>();

            // 右下の操作説明（リザルト画面の「A：つぎへ」と同じ場所）。
            // カードの間は背景を暗くしているので、白い字にする
            m_hint = ToyKit.UIText("Hint", root, m_font, HintText, 32f, Color.white);
            ToyKit.Anchor(m_hint.rectTransform, new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(600f, 50f));
            m_hint.rectTransform.pivot = new Vector2(1f, 0f);
            m_hint.alignment = TextAlignmentOptions.Right;
            m_hint.alpha = 0f;
        }

        void BuildCard(Transform root)
        {
            var card = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(root, false);
            card.anchorMin = card.anchorMax = new Vector2(1f, 0.5f);
            card.pivot = new Vector2(1f, 0.5f);
            card.sizeDelta = new Vector2(CardW, CardH);
            card.anchoredPosition = new Vector2(RestX, RestY);
            m_card = card;
            m_cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            m_cardGroup.alpha = 0f;

            // 外側の暗い縁 → レアリティ色の外枠 → クリーム色の下地
            var edge = ToyKit.UIRounded("Edge", card, Color.white, 26f);
            ToyKit.Stretch(edge);
            m_edge = edge.GetComponent<Image>();

            var border = ToyKit.UIRounded("Border", card, Color.white, 26f);
            ToyKit.Stretch(border);
            border.offsetMin = new Vector2(2f, 2f);
            border.offsetMax = new Vector2(-2f, -2f);
            m_border = border.GetComponent<Image>();

            var bg = ToyKit.UIRounded("Bg", card, CardColor, 24f);
            ToyKit.Stretch(bg);
            bg.offsetMin = new Vector2(7f, 7f);
            bg.offsetMax = new Vector2(-7f, -7f);
            var mask = bg.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // 下地のグラデーション（レアリティ色）と、横切る光
            var fill = ToyKit.UIImage("Fill", bg, new Color(1f, 1f, 1f, 0.10f), AchievementVisuals.CardGradient(AchievementRarity.Bronze, true));
            ToyKit.Stretch(fill);
            fill.GetComponent<Image>().type = Image.Type.Simple;
            m_fill = fill.GetComponent<Image>();

            var shine = ToyKit.UIImage("Shine", bg, new Color(1f, 1f, 1f, 0f), AchievementVisuals.CardShine());
            var shineRect = shine;
            shineRect.anchorMin = shineRect.anchorMax = new Vector2(0.5f, 0.5f);
            shineRect.sizeDelta = new Vector2(ShineW, CardH + 260f);
            shineRect.anchoredPosition = new Vector2(-CardW, 0f);
            shineRect.localEulerAngles = new Vector3(0f, 0f, ShineAngle);
            shine.GetComponent<Image>().type = Image.Type.Simple;
            m_shine = shine.GetComponent<Image>();
            m_shineRect = shineRect;

            // アイコン（レアリティの色つきアイコン）
            var icon = ToyKit.UIImage("Icon", card, Color.white, null);
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(190f, 190f);
            icon.anchoredPosition = new Vector2(-285f, 125f);
            m_icon = icon.GetComponent<Image>();
            m_icon.type = Image.Type.Simple;
            m_icon.preserveAspect = true;

            // 名前（レアリティ名の文字は出さない。色とアイコンで分かるようにする）
            m_name = ToyKit.UIText("Name", card, m_font, "", 48f, Ink);
            m_name.alignment = TextAlignmentOptions.Left;
            m_name.fontStyle = FontStyles.Bold;
            m_name.enableAutoSizing = true;
            m_name.fontSizeMin = 26f;
            m_name.fontSizeMax = 48f;
            m_name.textWrappingMode = TextWrappingModes.Normal;
            if (m_plainMat != null) m_name.fontSharedMaterial = m_plainMat;
            ToyKit.Anchor(m_name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(110f, 150f), new Vector2(540f, 110f));

            // 獲得日時（名前と同じ左はしにそろえる）
            m_date = ToyKit.UIText("Date", card, BodyFont, "", 24f, Ink);
            m_date.alignment = TextAlignmentOptions.Left;
            ToyKit.Anchor(m_date.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(110f, 72f), new Vector2(540f, 40f));

            // 区切り線
            var line = ToyKit.UIImage("Divider", card, new Color(Ink.r, Ink.g, Ink.b, 0.20f));
            ToyKit.Anchor(line, new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(CardW - 90f, 3f));

            // 解除条件（札と本文の左はしをそろえる）
            SectionTitle(card, "解除条件", new Vector2(-265f, -8f));
            m_condition = ToyKit.UIText("Condition", card, BodyFont, "", 26f, Ink);
            m_condition.alignment = TextAlignmentOptions.TopLeft;
            m_condition.textWrappingMode = TextWrappingModes.Normal;
            m_condition.enableAutoSizing = true;
            m_condition.fontSizeMin = 16f;
            m_condition.fontSizeMax = 26f;
            var condRect = m_condition.rectTransform;
            condRect.anchorMin = condRect.anchorMax = new Vector2(0.5f, 0.5f);
            condRect.pivot = new Vector2(0f, 1f);
            condRect.anchoredPosition = new Vector2(-340f, -36f);
            condRect.sizeDelta = new Vector2(680f, 84f);

            // 一言
            SectionTitle(card, "一言", new Vector2(-265f, -146f));
            m_poem = ToyKit.UIText("Poem", card, BodyFont, "", 26f, Ink);
            m_poem.alignment = TextAlignmentOptions.TopLeft;
            m_poem.textWrappingMode = TextWrappingModes.Normal;
            m_poem.enableAutoSizing = true;
            m_poem.fontSizeMin = 16f;
            m_poem.fontSizeMax = 26f;
            var poemRect = m_poem.rectTransform;
            poemRect.anchorMin = poemRect.anchorMax = new Vector2(0.5f, 0.5f);
            poemRect.pivot = new Vector2(0f, 1f);
            poemRect.anchoredPosition = new Vector2(-340f, -174f);
            poemRect.sizeDelta = new Vector2(680f, 70f);
        }

        // 「じっせき」と同じ橙の札
        void SectionTitle(Transform parent, string text, Vector2 pos)
        {
            var chip = ToyKit.UIRounded("Section_" + text, parent, ToyKit.Palette[1], 18f);
            ToyKit.Anchor(chip, new Vector2(0.5f, 0.5f), pos, new Vector2(150f, 38f));
            var label = ToyKit.UIText("Text", chip, m_font, text, 24f, Color.white);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 24f;
            ToyKit.Stretch(label.rectTransform);
        }

        void BuildProgress(Transform root)
        {
            var panel = new GameObject("Progress", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root, false);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.sizeDelta = new Vector2(700f, 220f);
            panel.anchoredPosition = new Vector2(RestX, RestY);
            m_progressGroup = panel.gameObject.AddComponent<CanvasGroup>();
            m_progressGroup.alpha = 0f;

            var bg = ToyKit.UIRounded("Bg", panel, CardColor, 24f);
            ToyKit.Stretch(bg);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = Ink;
            outline.effectDistance = new Vector2(3f, -3f);

            m_progressLabel = ToyKit.UIText("Label", panel, m_font, "", 34f, Ink);
            m_progressLabel.fontStyle = FontStyles.Bold;
            m_progressLabel.enableAutoSizing = true;
            m_progressLabel.fontSizeMin = 20f;
            m_progressLabel.fontSizeMax = 34f;
            if (m_plainMat != null) m_progressLabel.fontSharedMaterial = m_plainMat;
            ToyKit.Anchor(m_progressLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(620f, 60f));

            var gaugeBack = ToyKit.UIRounded("GaugeBack", panel, GaugeBack, 10f);
            ToyKit.Anchor(gaugeBack, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(540f, 24f));

            var fill = ToyKit.UIImage("GaugeFill", gaugeBack, AccentColor, AchievementVisuals.BarSprite());
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 1f;
            m_gaugeFill = fillImage;
        }
    }
}
