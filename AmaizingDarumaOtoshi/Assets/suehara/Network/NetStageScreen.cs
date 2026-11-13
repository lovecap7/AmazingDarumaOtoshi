using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ロビーで自分が遊びたいステージを選ぶ画面(NetSessionMenu の「ステージ」から開く)。見た目と操作はステージセレクト(ローカルの MatchSelect)と同じ:
//  ・右にステージのマス(最後におまかせ)、左にカーソルが乗っているステージのプレビュー(動かせる見た目だけのステージ。右スティック / IJKL で見回す)
//  ・自分のカーソルで A：そのステージに投票(試合を はじめるときに、みんなの票から抽選する)　B：もどる　Start：準備完了の切り替え
//  ・選んだマスに自分のコイン。ステージを変えると準備完了は外れる(ホストが外す)
public class NetStageScreen : MonoBehaviour
{
    const float kCursorSpeed = 1300f;
    const int kSortingOrder = 55;   // メニュー(50)の上
    // ステージセレクト(SelectScene の MatchSelect のローカル)と同じ並べ方
    const int kColumns = 3;
    static readonly Vector2 kTileAspect = new Vector2(14f, 9f);
    const float kPreviewX = -475f;
    static readonly Vector3 kPreviewOrigin = new Vector3(0f, -1000f, 0f);

    class Tile
    {
        public RectTransform rect;
        public Vector2 size;
        public StageData stage;
        public int index;   // NetCharacterList.stages の番号(おまかせは NetCharacterList.None)
    }

    public bool IsOpen { get; private set; }

    NetSelectLook m_look;
    RectTransform m_canvas, m_root, m_cursor, m_token, m_previewFrame;
    readonly List<Tile> m_tiles = new List<Tile>();
    TextMeshProUGUI m_previewLetter, m_previewName, m_description, m_status, m_hint;
    RawImage m_previewImage;
    Camera m_previewCam;
    RenderTexture m_previewTexture;
    Transform m_previewRoot;
    readonly Dictionary<StageData, GameObject> m_models = new Dictionary<StageData, GameObject>();
    SelectRoster.StagePreview m_previewInfo;
    Tile m_previewTile, m_placed;
    float m_yaw, m_pitch, m_idle;
    Vector2 m_cursorPos;
    int m_openFrame;
    bool m_fogWasOn;

    public void Open()
    {
        if (m_root == null) Build();
        IsOpen = true;
        m_openFrame = Time.frameCount;
        m_root.gameObject.SetActive(true);
        var current = CurrentTile();
        m_cursorPos = current != null ? current.rect.anchoredPosition : m_tiles.Count > 0 ? m_tiles[0].rect.anchoredPosition : Vector2.zero;
        m_previewTile = null;
        m_placed = null;
        MenuSounds.Decide();
    }

    public void Close()
    {
        IsOpen = false;
        if (m_root != null) m_root.gameObject.SetActive(false);
        if (m_previewCam != null) m_previewCam.enabled = false;
        if (m_previewRoot != null) m_previewRoot.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= FogOff;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= FogBack;
        if (m_canvas != null) Destroy(m_canvas.gameObject);
        if (m_previewRoot != null) Destroy(m_previewRoot.gameObject);
        if (m_previewCam != null) Destroy(m_previewCam.gameObject);
        if (m_previewTexture != null) m_previewTexture.Release();
    }

    void Update()
    {
        if (!IsOpen) return;
        var local = NetMatchPlayer.Local;
        if (local == null) { Close(); return; }
        float dt = Time.unscaledDeltaTime;

        if (m_openFrame != Time.frameCount)
        {
            var s = NetSelectLook.Read();
            m_cursorPos += s.move * kCursorSpeed * dt;
            m_cursorPos.x = Mathf.Clamp(m_cursorPos.x, -930f, 930f);
            m_cursorPos.y = Mathf.Clamp(m_cursorPos.y, -510f, 510f);
            if (s.look != Vector2.zero)
            {
                m_idle = 0f;
                m_yaw += s.look.x * 100f * dt;
                m_pitch = Mathf.Clamp(m_pitch - s.look.y * 45f * dt, -30f, 25f);
            }
            if (s.b) { MenuSounds.Cancel(); Close(); return; }
            if (s.start && !local.IsHostPlayer && local.InMatch)
            {
                bool ready = local.Status != NetMatchPlayer.NetStatus.Ready;
                local.RequestReady(ready);
                if (ready) MenuSounds.Decide(); else MenuSounds.Cancel();
            }
            else if (s.a)
            {
                var tile = TileAt(m_cursorPos);
                if (tile != null)
                {
                    MenuSounds.Decide();
                    local.RequestStage(tile.index);
                }
            }
        }

        var hover = TileAt(m_cursorPos);
        if (hover != null) ShowPreview(hover);
        UpdateLook(local, hover, dt);
        UpdatePreview(dt);
    }

    Tile CurrentTile()
    {
        var local = NetMatchPlayer.Local;
        var list = NetCharacterList.Instance;
        if (local == null || list == null) return null;
        int vote = list.Stage(local.StageVote) != null ? local.StageVote : NetCharacterList.None;
        return m_tiles.Find(t => t.index == vote);
    }

    Tile TileAt(Vector2 point) => m_tiles.Find(t => NetSelectLook.Contains(t.rect, t.size, point));

    void UpdateLook(NetMatchPlayer local, Tile hover, float dt)
    {
        float k = 1f - Mathf.Exp(-14f * dt);
        Color color = local.Color;
        m_cursor.anchoredPosition = m_cursorPos;
        m_cursor.GetComponent<Image>().color = color;
        foreach (var t in m_tiles) t.rect.localScale = Vector3.Lerp(t.rect.localScale, Vector3.one * (t == hover ? 1.08f : 1f), k);

        var current = CurrentTile();
        if (current != m_placed)
        {
            m_placed = current;
            if (current != null) NetSelectLook.PlaceToken(m_token, current.rect, current.size, local.Index);
            else m_token.gameObject.SetActive(false);
        }
        m_token.GetComponent<Image>().color = color;
        m_token.localScale = Vector3.Lerp(m_token.localScale, Vector3.one, k);
        m_previewFrame.localScale = Vector3.Lerp(m_previewFrame.localScale, Vector3.one, 1f - Mathf.Exp(-12f * dt));

        // 下の状態: いま投票しているステージと、準備完了
        string vote = current == null ? "まだ えらんでいません" : current.stage != null ? current.stage.displayName : m_look.Texts.randomStage;
        bool canReady = !local.IsHostPlayer && local.InMatch;
        bool ready = local.Status == NetMatchPlayer.NetStatus.Ready;
        m_status.text = $"{local.Label} の投票：{vote}" + (canReady ? (ready ? "　<color=#E8443F>じゅんびOK！</color>" : "") : "");
        m_hint.text = canReady
            ? "スティック：うごかす　A：投票する　Start：じゅんび完了　B：もどる"
            : "スティック：うごかす　A：投票する　B：もどる";
    }

    // カーソルが乗ったステージを左の枠に映す(変わったときだけ。ステージセレクトと同じ)
    void ShowPreview(Tile tile)
    {
        if (tile == m_previewTile) return;
        m_previewTile = tile;
        foreach (var m in m_models.Values) if (m != null) m.SetActive(false);
        var roster = NetCharacterList.Instance != null ? NetCharacterList.Instance.roster : null;
        m_previewInfo = tile.stage != null && roster != null ? roster.PreviewOf(tile.stage) : null;
        if (m_previewInfo != null)
        {
            if (!m_models.TryGetValue(tile.stage, out var model) || model == null)
            {
                model = Instantiate(m_previewInfo.model, m_previewRoot, false);
                // 見た目だけ(当たり判定は消す。試合のシーンの中に置くので)
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                m_models[tile.stage] = model;
            }
            model.SetActive(true);
        }
        m_yaw = 0f;
        m_pitch = 0f;
        m_idle = 0f;
        m_previewFrame.localScale = Vector3.one * 1.04f;

        var texts = m_look.Texts;
        m_previewImage.enabled = m_previewInfo != null;
        m_previewFrame.GetComponent<Image>().color = tile.stage == null ? NetSelectLook.RandomColor : tile.stage.color;
        m_previewLetter.text = m_previewInfo != null ? "" : tile.stage == null ? texts.randomLetter : NetSelectLook.Initial(tile.stage.displayName);
        m_previewName.text = tile.stage == null ? texts.randomStage : tile.stage.displayName;
        m_description.text = tile.stage == null ? texts.randomStageDescription : tile.stage.description;
    }

    // プレビューのカメラ: 本編のカメラと同じ距離・角度から、見ている点のまわりを回る(ステージセレクトと同じ)
    void UpdatePreview(float dt)
    {
        bool on = m_previewInfo != null;
        m_previewRoot.gameObject.SetActive(on);
        m_previewCam.enabled = on;
        if (!on) return;
        m_idle += dt;
        if (m_idle > 1.5f) m_yaw += 7f * dt;
        var rot = Quaternion.Euler(Mathf.Clamp(m_previewInfo.pitch + m_pitch, 15f, 85f), m_yaw, 0f);
        var target = kPreviewOrigin + m_previewInfo.target;
        m_previewCam.transform.SetPositionAndRotation(target - rot * Vector3.forward * m_previewInfo.distance, rot);
    }

    // 背景のフォグがプレビューにかかると白くかすむので、プレビューのカメラで撮る間だけ切る
    void FogOff(UnityEngine.Rendering.ScriptableRenderContext context, Camera c)
    {
        if (c != m_previewCam) return;
        m_fogWasOn = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    void FogBack(UnityEngine.Rendering.ScriptableRenderContext context, Camera c)
    {
        if (c == m_previewCam) RenderSettings.fog = m_fogWasOn;
    }

    //========================================
    // 組み立て(ステージセレクトと同じ大きさ・並び)
    //========================================

    void Build()
    {
        m_look = new NetSelectLook();
        var look = m_look;
        m_canvas = ToyKit.CreateOverlayCanvas("NetStageScreen", kSortingOrder);
        m_root = NetSelectLook.Layer("Root", m_canvas);
        var back = ToyKit.UIImage("Backdrop", m_root, NetSelectLook.Backdrop);
        ToyKit.Stretch(back);
        look.Header(m_root, look.Texts.stageHeader, ToyKit.Palette[4]);

        // ステージのマス(右半分。最後におまかせ)
        var list = NetCharacterList.Instance;
        var stages = new List<StageData>();
        var indices = new List<int>();
        for (int i = 0; list != null && i < list.stages.Count; i++)
        {
            if (list.stages[i] == null) continue;
            stages.Add(list.stages[i]);
            indices.Add(i);
        }
        int count = stages.Count + 1;
        float aspect = kTileAspect.y / kTileAspect.x;
        var grid = NetSelectLook.GridLayout(count, kColumns, new Vector2(900f, 790f), new Vector2(280f, 280f * aspect), 14f, 350f);
        for (int i = 0; i < count; i++)
        {
            bool random = i == stages.Count;
            var st = random ? null : stages[i];
            var rect = look.MakeTile(m_root, grid[i].pos + new Vector2(480f, 0f), grid[i].size, random ? NetSelectLook.RandomColor : st.color,
                                     random ? null : st.preview, random ? look.Texts.randomStage : st.displayName,
                                     random ? look.Texts.randomLetter : NetSelectLook.Initial(st.displayName));
            m_tiles.Add(new Tile { rect = rect, size = grid[i].size, stage = st, index = random ? NetCharacterList.None : indices[i] });
        }

        BuildPreview();

        // 操作の説明(右上。見出しと同じ高さ)
        m_hint = ToyKit.UIText("Hint", m_root, look.Font, "", 30f, ToyKit.Ink);
        m_hint.alignment = TextAlignmentOptions.Right;
        ToyKit.Anchor(m_hint.rectTransform, new Vector2(1f, 1f), new Vector2(-560f, -85f), new Vector2(1040f, 44f));

        var local = NetMatchPlayer.Local;
        string label = local != null ? local.Label : "1P";
        var cursors = NetSelectLook.Layer("Cursors", m_root);
        m_token = look.MakeToken(m_root, label, Color.white);
        m_cursor = look.MakeCursor(cursors, label, Color.white);
        m_root.gameObject.SetActive(false);
    }

    // 左のプレビュー(ステージセレクトの BuildStagePreview と同じ)
    void BuildPreview()
    {
        var look = m_look;
        m_previewRoot = new GameObject("NetStagePreview").transform;
        m_previewRoot.position = kPreviewOrigin;
        m_previewRoot.gameObject.SetActive(false);
        m_previewTexture = new RenderTexture(1280, 720, 24) { antiAliasing = 4, name = "NetStagePreview" };
        m_previewCam = new GameObject("NetStagePreviewCamera").AddComponent<Camera>();
        m_previewCam.targetTexture = m_previewTexture;
        m_previewCam.clearFlags = CameraClearFlags.SolidColor;
        m_previewCam.backgroundColor = new Color(0.85f, 0.9f, 0.96f);
        m_previewCam.fieldOfView = 40f;
        m_previewCam.nearClipPlane = 0.3f;
        m_previewCam.farClipPlane = 2000f;
        m_previewCam.enabled = false;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += FogOff;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += FogBack;

        m_previewFrame = ToyKit.UIImage("PreviewFrame", m_root, NetSelectLook.RandomColor, look.UiSprite);
        m_previewFrame.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(m_previewFrame, new Vector2(0.5f, 0.5f), new Vector2(kPreviewX, 85f), new Vector2(880f, 495f));
        var ol = m_previewFrame.gameObject.AddComponent<Outline>(); ol.effectColor = ToyKit.Ink; ol.effectDistance = new Vector2(3f, -3f);

        m_previewImage = new GameObject("View", typeof(RectTransform)).AddComponent<RawImage>();
        m_previewImage.rectTransform.SetParent(m_previewFrame, false);
        ToyKit.Stretch(m_previewImage.rectTransform);
        m_previewImage.rectTransform.offsetMin = new Vector2(10f, 10f);
        m_previewImage.rectTransform.offsetMax = new Vector2(-10f, -10f);
        m_previewImage.texture = m_previewTexture;
        m_previewImage.enabled = false;

        m_previewLetter = ToyKit.UIText("Letter", m_previewFrame, look.Font, "", 220f, Color.white);
        m_previewLetter.fontSharedMaterial = look.LetterMat;
        ToyKit.Stretch(m_previewLetter.rectTransform);

        var tip = ToyKit.UIText("Tip", m_previewFrame, look.Font, look.Texts.previewTip, 26f, Color.white);
        tip.fontSharedMaterial = look.LetterMat;
        tip.alignment = TextAlignmentOptions.BottomRight;
        ToyKit.Stretch(tip.rectTransform);
        tip.rectTransform.offsetMin = new Vector2(20f, 18f);
        tip.rectTransform.offsetMax = new Vector2(-24f, -18f);

        // 名前・説明の後ろに敷く薄い板
        var plate = ToyKit.UIImage("InfoPlate", m_root, new Color(1f, 0.98f, 0.94f, 0.78f), look.UiSprite);
        plate.GetComponent<Image>().pixelsPerUnitMultiplier = 0.4f;
        ToyKit.Anchor(plate, new Vector2(0.5f, 0.5f), new Vector2(kPreviewX, -300f), new Vector2(900f, 270f));

        m_previewName = ToyKit.UIText("Name", m_root, look.Font, "", 64f, ToyKit.Ink);
        m_previewName.fontSharedMaterial = look.PopMat;
        ToyKit.Anchor(m_previewName.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(kPreviewX, -215f), new Vector2(880f, 90f));

        m_description = ToyKit.UIText("Description", m_root, look.Font, "", 30f, ToyKit.Ink);
        m_description.textWrappingMode = TextWrappingModes.Normal;
        ToyKit.Anchor(m_description.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(kPreviewX, -300f), new Vector2(880f, 90f));
        m_status = ToyKit.UIText("Status", m_root, look.Font, "", 38f, ToyKit.Ink);
        ToyKit.Anchor(m_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(kPreviewX, -385f), new Vector2(880f, 60f));
    }
}
