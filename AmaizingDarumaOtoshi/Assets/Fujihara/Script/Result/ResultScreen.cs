using System;
using System.Collections;
using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// リザルト画面（1位の表彰）。「積み木の表彰台」の演出。
//  1. 暗転した中、スポットライトが1位の場所を照らす
//  2. 積み木が上から1個ずつ落ちてきて「コトン、コトン」と表彰台になる。てっぺんに1位の顔が乗る
//  3. ハンマーで表彰台の一番下を「カコーン！」と叩き出す（だるま落とし）。上が1段落ちてピタッと着地
//  4. 明るくなって紙吹雪。積み木が並んで「1P のかち！」の文字になる。2位以下は低い台の上から見上げる
// 試合の結果は MatchResultRelay が ResultData に入れておく。このシーンだけを再生したときは確認用の仮の結果で動く。
// 舞台はタイトル画面と同じく、ToyKit で実行時に組み立てる（シーンにはこのコンポーネントを置くだけ）。
public class ResultScreen : MonoBehaviour
{
    [Header("素材")]
    [Tooltip("文字のフォント（未設定なら PresentationSettings のフォント）")]
    [SerializeField] TMP_FontAsset font;

    [Header("シーン")]
    [Tooltip("決定ボタンで移るシーン")]
    [SerializeField] string nextSceneName = "SelectScene";

    [Header("表彰台")]
    [Tooltip("1位の表彰台の高さ（積み木の数）。試合が終わったときの積み木の数を、この範囲に収める")]
    [SerializeField, Range(2, 8)] int minWinnerBlocks = 3;
    [SerializeField, Range(2, 10)] int maxWinnerBlocks = 6;
    [Tooltip("表彰台どうしの間隔")]
    [SerializeField] float podiumSpacing = 2.9f;
    [SerializeField] float hammerScale = 0.75f;

    [Header("文字")]
    [Tooltip("勝った人の名前（1P など）のあとに続く文字。1文字ずつ積み木になる")]
    [SerializeField] string winText = "のかち！";
    [Tooltip("引き分けのときの文字")]
    [SerializeField] string drawText = "ひきわけ！";

    [Header("演出")]
    [Tooltip("「コトン」「カコーン！」などの擬音（字幕）をこの画面で出す")]
    [SerializeField] bool showOnomatopoeia = true;
    [Tooltip("演出中に決定ボタンを押したときの早送りの速さ")]
    [SerializeField] float skipSpeed = 5f;
    [Tooltip("演出が終わってから、この秒数操作がないと自動で次へ（0で無効）")]
    [SerializeField] float autoReturnDelay = 30f;

    [Header("確認用（このシーンだけを再生したとき）")]
    [Tooltip("仮の結果に使うキャラクター（未設定ならキャラクターなし）")]
    [SerializeField] SelectRoster previewRoster;
    [SerializeField, Range(1, 4)] int previewPlayers = 4;
    [SerializeField] bool previewDraw = false;

    // ---- 寸法 ----
    const float BlockW = ToyKit.BlockW, BlockH = ToyKit.BlockH, BlockD = ToyKit.BlockD;
    const float HeadR = ToyKit.HeadR;
    const float TextRowY = 6.1f;        // 「1P のかち！」の段の高さ
    const float TextRowZ = 2.2f;
    const float LoserZ = 0.6f;          // 2位以下は少し奥に並べる
    const float IdleYaw = -32f;         // ハンマーの待ちの角度
    const float SpotPower = 160f;       // スポットライトの明るさ
    static Color[] Palette => ToyKit.Palette;

    // 1人分の表彰台
    class Podium
    {
        public ResultData.Entry entry;
        public Transform column;                            // 積み木と顔をまとめた親（足元が原点）
        public readonly List<Transform> blocks = new List<Transform>();   // 下から
        public ToyKit.Face face;
        public TextMeshPro tag;                             // 頭の上の「1P」
        public Vector3 headBase;                            // 顔の位置（column の中）
        public bool shown;                                  // 顔を見せたか（2位以下）
    }

    // ---- 結果 ----
    readonly List<ResultData.Entry> entries = new List<ResultData.Entry>();
    bool isDraw;

    // ---- 舞台 ----
    Camera cam;
    DepthOfField dof;
    Light keyLight, rimLight, spot;
    Material skyMat;
    readonly List<Renderer> shafts = new List<Renderer>();
    Color ambSky, ambEquator, ambGround;
    Mesh blockMesh;
    Material letterMat, popMat;
    ParticleSystem confetti, puff, sparkle;
    Transform groundRing;
    Material ringMat;
    Transform hammer;
    readonly List<Podium> podiums = new List<Podium>();
    readonly List<Transform> textBlocks = new List<Transform>();
    readonly List<Vector3> textBase = new List<Vector3>();

    // ---- UI ----
    RectTransform uiRoot;
    CanvasGroup infoGroup, hintGroup;
    readonly List<GameObject> pops = new List<GameObject>();

    // ---- 動き ----
    float baseSpeed = 1f, slow = 1f;    // 早送り・スローモーション
    float lightLevel, lightTarget;      // 0 = 暗転、1 = 明るい
    float spotLevel, spotTarget;
    Vector3 camPos, camLook, curPos, curLook;
    float camFov = 40f, curFov = 40f, dofAperture = 2.8f;
    float shake;
    Vector3 hammerPos;
    float hammerYaw = IdleYaw;
    bool hammerShown, swinging;
    bool winnerHappy, textShown;
    bool done, leaving;
    float doneTime;

    // 演出の時間（早送り・スローモーションをかける）
    float Dt => Time.unscaledDeltaTime * baseSpeed * slow;

    // ================= 始まり =================
    void Start()
    {
        // 試合の終わりで止まった時間と、MatchResultRelay が止めた「セレクトへ戻る」を元に戻す
        Time.timeScale = 1f;
        DarumaMatch.AutoReturnToSelect = true;

        if (font == null) font = PresentationSettings.DefaultFont;
        LoadResult();
        BuildStage();
        BuildPodiums();
        BuildUI();
        ApplyLights();
        StartCoroutine(Ceremony());
    }

    // 試合の結果を読む（無ければ確認用の仮の結果）
    void LoadResult()
    {
        if (ResultData.HasResult)
        {
            entries.AddRange(ResultData.Ranking);
            isDraw = ResultData.IsDraw;
            ResultData.Clear();
            return;
        }

        string[] labels = { "1P", "2P", "CPU1", "CPU2" };
        for (int i = 0; i < previewPlayers; i++)
        {
            CharacterData chara = null;
            if (previewRoster != null && previewRoster.characters.Count > 0)
                chara = previewRoster.characters[i % previewRoster.characters.Count];
            entries.Add(new ResultData.Entry
            {
                label = labels[i],
                playerColor = Palette[(i * 2) % Palette.Length],
                character = chara,
                rank = previewDraw && i < 2 ? 1 : i + 1,
                blocks = 5,
                knocks = 6 - i,
                kos = 3 - i / 2,
            });
        }
        isDraw = previewDraw && previewPlayers >= 2;
    }

    // ================= 舞台づくり =================
    void BuildStage()
    {
        blockMesh = ToyKit.RoundedBox(new Vector3(BlockW, BlockH, BlockD), 0.14f, 10);
        letterMat = ToyKit.LetterMaterial(font);
        popMat = ToyKit.PopMaterial(letterMat);

        cam = ToyKit.CreateCamera();
        ToyKit.CreateLights();
        dof = ToyKit.CreatePostProcess(0.35f).dof;
        ToyKit.CreateEnvironment(blockMesh);

        // 暗転させるために、ToyKit が作ったライト・空・光の筋を覚えておく
        keyLight = GameObject.Find("KeyLight").GetComponent<Light>();
        rimLight = GameObject.Find("RimLight").GetComponent<Light>();
        skyMat = GameObject.Find("Sky").GetComponent<Renderer>().sharedMaterial;
        foreach (var go in gameObject.scene.GetRootGameObjects())
            if (go.name == "LightShaft") shafts.Add(go.GetComponent<Renderer>());
        ambSky = RenderSettings.ambientSkyColor;
        ambEquator = RenderSettings.ambientEquatorColor;
        ambGround = RenderSettings.ambientGroundColor;

        // 1位を照らすスポットライト
        spot = new GameObject("Spotlight").AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.95f, 0.85f);
        spot.range = 30f;
        spot.spotAngle = 30f;
        spot.innerSpotAngle = 16f;
        spot.shadows = LightShadows.Soft;
        spot.transform.position = new Vector3(0f, 11f, -3.5f);
        spot.transform.LookAt(new Vector3(0f, 1.5f, 0f));

        var fx = ToyKit.CreateFx();
        confetti = fx.confetti; puff = fx.puff; sparkle = fx.sparkle;
        groundRing = fx.groundRing; ringMat = fx.ringMat;

        hammer = ToyKit.CreateHammer();
        hammer.localScale = Vector3.one * hammerScale;
        hammer.gameObject.SetActive(false);
    }

    void BuildPodiums()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var p = new Podium { entry = e };
            bool top = e.rank == 1;
            bool winner = i == 0 && !isDraw;

            // 表彰台の高さ：1位は試合の積み木の数＋叩き出す1個、引き分けの1位は3、2位は2、3位以下は1
            int count = winner ? Mathf.Clamp(e.blocks, minWinnerBlocks, Mathf.Max(minWinnerBlocks, maxWinnerBlocks)) + 1
                      : top ? 3 : e.rank == 2 ? 2 : 1;

            p.column = new GameObject("Podium_" + e.label).transform;
            p.column.position = new Vector3(SlotX(i), 0f, top ? 0f : LoserZ);

            for (int b = 0; b < count; b++)
            {
                // 順位の数字は一番下の積み木に書く（1位は叩き出したあとに一番下になる2個目に書く）
                string label = (winner ? b == 1 : b == 0) ? e.rank.ToString() : "";
                var t = ToyKit.CreateBlock(label, Palette[(b * 2 + i) % Palette.Length], blockMesh, new Vector2(1.05f, 0.68f), BlockD, font, letterMat, out _);
                t.SetParent(p.column, false);
                t.localPosition = new Vector3(0f, (b + 0.5f) * BlockH, 0f);
                t.localRotation = Quaternion.Euler(0f, winner ? 0f : UnityEngine.Random.Range(-6f, 6f), 0f);
                p.blocks.Add(t);
                if (top) t.gameObject.SetActive(false);   // 1位の積み木は上から落ちてくる
            }

            p.face = ToyKit.CreateHead();
            p.face.root.SetParent(p.column, false);
            p.headBase = new Vector3(0f, count * BlockH + HeadR, 0f);
            p.face.root.localPosition = p.headBase;
            TintHead(p.face, e);
            p.face.root.gameObject.SetActive(false);

            p.tag = ToyKit.Text3D(font, letterMat, e.label, p.column, new Vector2(2f, 0.5f), 3f);
            p.tag.color = e.playerColor;
            p.tag.transform.localPosition = p.headBase + Vector3.up * (HeadR + 0.4f);
            p.tag.gameObject.SetActive(false);

            podiums.Add(p);
        }
    }

    // 並べる位置：1位が真ん中、2位が左、3位が右、4位がその外の左…
    float SlotX(int i)
    {
        if (i == 0) return 0f;
        int k = (i + 1) / 2;
        return (i % 2 == 1 ? -1f : 1f) * k * podiumSpacing;
    }

    // 顔の色をキャラクターの色にする（白＝未設定なら黄色のまま）
    static void TintHead(ToyKit.Face face, ResultData.Entry e)
    {
        if (e.character == null || e.character.color == Color.white) return;
        face.body.SetColor("_BaseColor", e.character.color);
    }

    // ---------------- UI ----------------
    void BuildUI()
    {
        uiRoot = ToyKit.CreateOverlayCanvas("UI", 10);

        // 下の帯：1位のキャラクター名と記録
        var info = new GameObject("Info", typeof(RectTransform)).GetComponent<RectTransform>();
        info.SetParent(uiRoot, false);
        ToyKit.Anchor(info, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1400f, 200f));
        infoGroup = info.gameObject.AddComponent<CanvasGroup>();
        infoGroup.alpha = 0f;

        var winner = entries.Count > 0 ? entries[0] : null;
        string name = isDraw ? "" : winner == null ? "" : winner.character != null ? winner.character.displayName : winner.label;
        var nameText = ToyKit.UIText("Name", info, font, name, 72f, Color.white);
        nameText.fontSharedMaterial = letterMat;
        ToyKit.Anchor(nameText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1400f, 100f));

        string stats = isDraw || winner == null ? "" : $"くずした　{winner.knocks}　　とばした　{winner.kos}";
        var statsText = ToyKit.UIText("Stats", info, font, stats, 36f, Color.white);
        statsText.fontSharedMaterial = letterMat;
        ToyKit.Anchor(statsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1400f, 60f));

        var hint = ToyKit.UIText("Hint", uiRoot, font, "A：つぎへ", 32f, Color.white);
        hint.fontSharedMaterial = letterMat;
        ToyKit.Anchor(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(500f, 50f));
        hint.rectTransform.pivot = new Vector2(1f, 0f);
        hint.alignment = TextAlignmentOptions.Right;
        hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;
    }

    // ================= 演出の流れ =================
    IEnumerator Ceremony()
    {
        var first = podiums[0];

        // ---- 1. 暗転 → スポットライト ----
        SetCam(new Vector3(-1.4f, 1.4f, -6.5f), new Vector3(0f, 0.9f, 0f), 40f, true);
        yield return Wait(0.8f);
        spotTarget = spotLevel = 1f;   // パッと点ける
        shake = 0.12f;
        yield return Wait(0.6f);

        if (isDraw)
        {
            yield return DrawCeremony();
            yield break;
        }

        // ---- 2. 積み木が落ちてきて表彰台になる ----
        int n = first.blocks.Count;
        for (int b = 0; b < n; b++)
        {
            int index = b;
            float topY = (b + 1) * BlockH;
            SetCam(new Vector3(-1.8f, topY + 1.0f, -6.2f - topY * 0.35f), new Vector3(0f, topY - 0.1f, 0f), 40f, false);
            var blk = first.blocks[b];
            blk.gameObject.SetActive(true);
            StartCoroutine(DropIn(blk, blk.localPosition, 5f, 0.3f, () =>
            {
                Vector3 at = first.column.TransformPoint(blk.localPosition) - Vector3.up * BlockH * 0.5f;
                ToyKit.Emit(puff, at, Quaternion.Euler(-90f, 0f, 0f), 4);
                shake = Mathf.Max(shake, 0.08f);
                if (index % 2 == 0) Pop("コトン", at + new Vector3(1.1f, 0.3f, 0f), 50f, Palette[(index + 2) % Palette.Length], UnityEngine.Random.Range(-12f, 12f));
            }));
            yield return Wait(Mathf.Lerp(0.34f, 0.18f, b / (float)Mathf.Max(1, n - 1)));   // だんだん速く
        }
        yield return Wait(0.4f);

        // てっぺんに顔が落ちてくる
        float headTop = n * BlockH + HeadR * 2f;
        SetCam(new Vector3(-1.8f, headTop + 0.6f, -6.4f - headTop * 0.3f), new Vector3(0f, headTop - 0.4f, 0f), 40f, false);
        first.face.root.gameObject.SetActive(true);
        yield return DropIn(first.face.root, first.headBase, 4f, 0.3f, () =>
        {
            Pop("ポテッ", first.column.TransformPoint(first.headBase) + new Vector3(1.0f, 0.4f, 0f), 60f, Palette[2], 12f);
            shake = Mathf.Max(shake, 0.1f);
        });
        yield return Wait(0.5f);

        // ---- 3. ハンマーで一番下を叩き出す ----
        Vector3 contact = first.column.position + new Vector3(-BlockW * 0.5f, BlockH * 0.5f, 0f);
        Vector3 pivot = ToyKit.HammerPivot(contact, hammerScale);
        SetCam(new Vector3(-3.2f, 2.2f, -8.8f), new Vector3(-0.3f, 1.9f, 0f), 40f, false);
        hammer.gameObject.SetActive(true);
        hammerShown = true;
        yield return Slide(pivot + new Vector3(-5f, 0f, -1f), pivot, 0.45f);
        yield return Wait(0.5f);   // ため

        yield return Swing(0.35f, -85f);
        Knock(first, contact);
        yield return ColumnDrop(first);
        winnerHappy = true;
        first.face.SetHappy(true);
        yield return Wait(0.35f);

        // ---- 4. 明るくなって紙吹雪、2位以下が出てくる、「1P のかち！」 ----
        yield return Celebrate(first.entry.label + winText);
    }

    // 引き分け：1位の人たちの表彰台が同時に積み上がる
    IEnumerator DrawCeremony()
    {
        var tops = podiums.FindAll(p => p.entry.rank == 1);
        SetCam(new Vector3(0f, 2.4f, -9.5f), new Vector3(0f, 1.5f, 0f), 42f, false);
        int max = 0;
        foreach (var p in tops) max = Mathf.Max(max, p.blocks.Count);
        for (int b = 0; b < max; b++)
        {
            foreach (var p in tops)
            {
                if (b >= p.blocks.Count) continue;
                var blk = p.blocks[b];
                blk.gameObject.SetActive(true);
                StartCoroutine(DropIn(blk, blk.localPosition, 5f, 0.3f, () =>
                    ToyKit.Emit(puff, p.column.TransformPoint(blk.localPosition), Quaternion.Euler(-90f, 0f, 0f), 3)));
            }
            yield return Wait(0.3f);
        }
        foreach (var p in tops)
        {
            p.face.root.gameObject.SetActive(true);
            StartCoroutine(DropIn(p.face.root, p.headBase, 4f, 0.3f, null));
        }
        yield return Wait(0.6f);
        yield return Celebrate(drawText);
    }

    // 明るくして、紙吹雪・残りの人・文字の積み木を出す
    IEnumerator Celebrate(string text)
    {
        lightTarget = 1f;
        spotTarget = 0.5f;
        SetCam(new Vector3(0f, 3.4f, -12.5f), new Vector3(0f, 3.2f, 0.5f), 42f, false);
        dofAperture = 9f;   // 全体を見せるのでボケを弱く
        ConfettiCannons();
        StartCoroutine(ConfettiRain(3f));

        // 2位以下（引き分けでないなら、1位以外）が、低い台の上にポンと出てくる
        yield return Wait(0.3f);
        foreach (var p in podiums)
        {
            if (p.face.root.gameObject.activeSelf) { p.tag.gameObject.SetActive(true); continue; }
            StartCoroutine(PopIn(p));
            yield return Wait(0.12f);
        }

        // 文字の積み木が1個ずつ落ちてきて並ぶ（最初のかたまりは名前「1P」など）
        yield return Wait(0.2f);
        yield return TextRow(text);
        Pop(isDraw ? "ドンッ！" : "ドンッ！！", new Vector3(-4.6f, TextRowY + 0.9f, TextRowZ), 120f, Palette[0], -8f, 0.9f);
        shake = 0.35f;
        ToyKit.Emit(sparkle, new Vector3(0f, TextRowY, TextRowZ - 0.8f), Quaternion.Euler(-90f, 0f, 0f), 40);

        // 早送りしていたら、ここで普通の速さに戻す
        baseSpeed = 1f;
        done = true;
        doneTime = Time.unscaledTime;
    }

    // ================= 部品の動き =================
    // 上から落ちてきて、着地でつぶれる（local は親の中の位置）
    IEnumerator DropIn(Transform t, Vector3 local, float height, float time, Action onLand)
    {
        for (float u = 0f; u < 1f; u += Dt / time)
        {
            t.localPosition = local + Vector3.up * height * (1f - u * u);
            yield return null;
        }
        t.localPosition = local;
        onLand?.Invoke();
        Vector3 baseScale = t.localScale;
        for (float u = 0f; u < 1f; u += Dt / 0.3f)
        {
            t.localScale = Vector3.Scale(baseScale, ToyKit.SquashVec(ToyKit.Squash(u, 0.15f)));
            yield return null;
        }
        t.localScale = baseScale;
    }

    // ハンマーを横から滑らせて構える
    IEnumerator Slide(Vector3 from, Vector3 to, float time)
    {
        for (float u = 0f; u < 1f; u += Dt / time)
        {
            hammerPos = Vector3.LerpUnclamped(from, to, ToyKit.EaseOutBack(u));
            yield return null;
        }
        hammerPos = to;
    }

    // 振りかぶって叩く（タイトル画面と同じ動き）
    IEnumerator Swing(float windup, float windupYaw)
    {
        swinging = true;
        float from = hammerYaw;
        for (float u = 0f; u < 1f; u += Dt / windup)
        {
            hammerYaw = Mathf.Lerp(from, windupYaw, 1f - (1f - u) * (1f - u));
            yield return null;
        }
        const float strike = 0.07f;
        for (float u = 0f; u < 1f; u += Dt / strike)
        {
            hammerYaw = Mathf.Lerp(windupYaw, 0f, u * u);
            yield return null;
        }
        hammerYaw = 0f;
        swinging = false;
    }

    // 一番下の積み木を叩き出す
    void Knock(Podium p, Vector3 contact)
    {
        var blk = p.blocks[0];
        p.blocks.RemoveAt(0);
        blk.SetParent(null, true);
        StartCoroutine(Fly(blk));

        // 残りを1段下げる準備：中身を1段下げて親を1段上げる（見た目は変わらない）。このあと親を落とす
        foreach (Transform child in p.column) child.localPosition -= Vector3.up * BlockH;
        p.headBase -= Vector3.up * BlockH;
        p.column.position += Vector3.up * BlockH;

        Pop("カコーン！", contact + new Vector3(-0.4f, 1.3f, -0.3f), 110f, Palette[1], -8f);
        ToyKit.Emit(confetti, contact, Quaternion.Euler(-30f, 90f, 0f), 40);
        ToyKit.Emit(sparkle, contact, Quaternion.Euler(0f, 90f, 0f), 20);
        shake = 0.6f;
        StartCoroutine(SlowMo(0.2f, 0.35f));
    }

    // 叩き出された積み木が回りながら飛んでいく
    IEnumerator Fly(Transform blk)
    {
        Vector3 v = new Vector3(11f, 4.5f, -1.5f);
        Vector3 spin = new Vector3(120f, 260f, -540f);
        for (float time = 0f; time < 2f; time += Dt)
        {
            v.y -= 22f * Dt;
            blk.position += v * Dt;
            blk.Rotate(spin * Dt, Space.World);
            yield return null;
        }
        Destroy(blk.gameObject);
    }

    // 上に残った山が1段落ちて、ピタッと着地
    IEnumerator ColumnDrop(Podium p)
    {
        Vector3 start = p.column.position;
        Vector3 end = start - Vector3.up * BlockH;
        const float time = 0.12f;
        for (float u = 0f; u < 1f; u += Dt / time)
        {
            p.column.position = Vector3.Lerp(start, end, u * u);
            yield return null;
        }
        p.column.position = end;

        Pop("ストン", end + new Vector3(1.4f, 0.5f, 0f), 64f, Palette[4], 10f);
        ToyKit.Emit(puff, end + Vector3.up * 0.05f, Quaternion.Euler(-90f, 0f, 0f), 12);
        StartCoroutine(Ring(end + Vector3.up * 0.02f));
        shake = Mathf.Max(shake, 0.3f);
        for (float u = 0f; u < 1f; u += Dt / 0.35f)
        {
            p.column.localScale = ToyKit.SquashVec(ToyKit.Squash(u, 0.12f));
            yield return null;
        }
        p.column.localScale = Vector3.one;
    }

    // 床に広がる輪
    IEnumerator Ring(Vector3 at)
    {
        groundRing.position = at;
        for (float u = 0f; u < 1f; u += Dt / 0.45f)
        {
            groundRing.localScale = Vector3.one * Mathf.Lerp(0.5f, 5f, 1f - (1f - u) * (1f - u));
            ringMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.8f * (1f - u)));
            yield return null;
        }
        groundRing.localScale = Vector3.zero;
    }

    // 2位以下の顔がポンと出てくる
    IEnumerator PopIn(Podium p)
    {
        p.face.root.gameObject.SetActive(true);
        p.tag.gameObject.SetActive(true);
        p.shown = true;
        for (float u = 0f; u < 1f; u += Dt / 0.35f)
        {
            p.face.root.localScale = Vector3.one * ToyKit.EaseOutBack(u);
            yield return null;
        }
        p.face.root.localScale = Vector3.one;
    }

    // 文字の積み木を1個ずつ落として横に並べる。最初のかたまり（名前）は1個の積み木にまとめる
    IEnumerator TextRow(string text)
    {
        var tokens = new List<string>();
        if (!isDraw && entries.Count > 0)
        {
            tokens.Add(entries[0].label);
            text = text.Substring(Mathf.Min(entries[0].label.Length, text.Length));
        }
        foreach (char c in text) if (!char.IsWhiteSpace(c)) tokens.Add(c.ToString());

        const float step = 1.32f;
        float x0 = -(tokens.Count - 1) * step * 0.5f;
        for (int i = 0; i < tokens.Count; i++)
        {
            // 名前の積み木は勝った人の色、それ以外は色を順番に
            Color color = i == 0 && !isDraw ? entries[0].playerColor : Palette[i % Palette.Length];
            var t = ToyKit.CreateBlock(tokens[i], color, blockMesh, new Vector2(1.05f, 0.68f), BlockD, font, letterMat, out _);
            var pos = new Vector3(x0 + i * step, TextRowY, TextRowZ);
            textBlocks.Add(t);
            textBase.Add(pos);
            StartCoroutine(DropIn(t, pos, 4f, 0.25f, () => shake = Mathf.Max(shake, 0.06f)));
            yield return Wait(0.13f);
        }
        yield return Wait(0.3f);
        textShown = true;
    }

    void ConfettiCannons()
    {
        ToyKit.Emit(confetti, new Vector3(-6f, 0.3f, 0.5f), Quaternion.LookRotation(new Vector3(0.45f, 1f, 0f)), 120);
        ToyKit.Emit(confetti, new Vector3(6f, 0.3f, 0.5f), Quaternion.LookRotation(new Vector3(-0.45f, 1f, 0f)), 120);
    }

    // 上から紙吹雪を降らせ続ける
    IEnumerator ConfettiRain(float time)
    {
        for (float t = 0f; t < time; t += 0.2f)
        {
            var at = new Vector3(UnityEngine.Random.Range(-6f, 6f), 8f, UnityEngine.Random.Range(-1f, 2f));
            ToyKit.Emit(confetti, at, Quaternion.Euler(90f, 0f, 0f), 20);
            yield return Wait(0.2f);
        }
    }

    IEnumerator SlowMo(float scale, float realTime)
    {
        slow = scale;
        for (float t = 0f; t < realTime; t += Time.unscaledDeltaTime) yield return null;
        slow = 1f;
    }

    IEnumerator Wait(float time)
    {
        for (float t = 0f; t < time; t += Dt) yield return null;
    }

    void SetCam(Vector3 pos, Vector3 look, float fov, bool snap)
    {
        camPos = pos; camLook = look; camFov = fov;
        if (snap) { curPos = pos; curLook = look; curFov = fov; }
    }

    void Pop(string text, Vector3 world, float size, Color color, float angle, float hold = 0.45f)
    {
        // Inspector でオフにしているときはこの画面の擬音を出さない
        if (!showOnomatopoeia) return;
        var local = ToyKit.WorldToCanvas(cam, uiRoot, world);
        ToyKit.Pop(this, uiRoot, font, popMat, text, local, size, color, angle, hold, pops);
    }

    // ================= 毎フレーム =================
    void Update()
    {
        float raw = Time.unscaledDeltaTime;
        HandleInput();
        UpdateLights(raw);
        UpdateCharacters();
        UpdateHammer();
        UpdateCamera(raw);

        infoGroup.alpha = Mathf.MoveTowards(infoGroup.alpha, textShown && !isDraw ? 1f : 0f, raw * 3f);
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, done ? 1f : 0f, raw * 3f);
    }

    void HandleInput()
    {
        if (leaving) return;
        bool submit = MenuInput.Submit() || MenuInput.AnyClick();
        if (!done)
        {
            // 演出中に押したら早送り
            if (submit) baseSpeed = skipSpeed;
            return;
        }
        bool timeout = autoReturnDelay > 0f && Time.unscaledTime - doneTime > autoReturnDelay;
        if ((submit && Time.unscaledTime - doneTime > 0.4f) || timeout)
        {
            leaving = true;
            Fader.Load(nextSceneName);
        }
    }

    void UpdateLights(float raw)
    {
        lightLevel = Mathf.MoveTowards(lightLevel, lightTarget, raw * 1.6f);
        spotLevel = Mathf.MoveTowards(spotLevel, spotTarget, raw * 2f);
        ApplyLights();
    }

    // 暗転の度合い（lightLevel）を、ライト・環境光・背景・空に反映する
    void ApplyLights()
    {
        float k = lightLevel;
        keyLight.intensity = 1.5f * k;
        rimLight.intensity = 1.1f * k;
        float amb = Mathf.Lerp(0.08f, 1f, k);
        RenderSettings.ambientSkyColor = ambSky * amb;
        RenderSettings.ambientEquatorColor = ambEquator * amb;
        RenderSettings.ambientGroundColor = ambGround * amb;
        var bg = Color.Lerp(new Color(0.05f, 0.04f, 0.06f), ToyKit.Horizon, k);
        cam.backgroundColor = bg;
        RenderSettings.fogColor = bg;
        skyMat.SetColor("_BaseColor", Color.Lerp(new Color(0.08f, 0.07f, 0.1f), Color.white, k));
        foreach (var r in shafts) r.enabled = k > 0.7f;
        spot.intensity = spotLevel * SpotPower * Mathf.Lerp(1f, 0.4f, k);
    }

    void UpdateCharacters()
    {
        float time = Time.unscaledTime;
        var first = podiums[0];

        // 1位：うれしそうにぴょんぴょん跳ねる
        if (winnerHappy)
        {
            float hop = Mathf.Abs(Mathf.Sin(time * 5f)) * 0.18f;
            first.face.root.localPosition = first.headBase + Vector3.up * hop;
            first.face.root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2.5f) * 8f);
        }

        // 2位以下：1位を見上げる
        Vector3 winnerHead = first.column.TransformPoint(first.headBase);
        foreach (var p in podiums)
        {
            if (!p.shown || isDraw) continue;
            Vector3 dir = p.face.root.position - winnerHead;   // 顔は -Z が正面なので、+Z を反対向きに向ける
            var look = Quaternion.LookRotation(dir.normalized);
            p.face.root.rotation = Quaternion.Slerp(p.face.root.rotation, Quaternion.Slerp(Quaternion.identity, look, 0.55f), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        }

        // 文字の積み木：ゆらゆら
        if (textShown)
            for (int i = 0; i < textBlocks.Count; i++)
                textBlocks[i].localPosition = textBase[i] + Vector3.up * Mathf.Sin(time * 2f + i * 0.7f) * 0.06f;
    }

    void UpdateHammer()
    {
        if (!hammerShown) return;
        if (!swinging) hammerYaw = Mathf.Lerp(hammerYaw, IdleYaw + Mathf.Sin(Time.unscaledTime * 1.7f) * 3f, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        hammer.position = hammerPos;
        hammer.rotation = Quaternion.Euler(0f, hammerYaw, 0f);
    }

    void UpdateCamera(float raw)
    {
        float k = 1f - Mathf.Exp(-3.5f * raw * baseSpeed);
        curPos = Vector3.Lerp(curPos, camPos, k);
        curLook = Vector3.Lerp(curLook, camLook, k);
        curFov = Mathf.Lerp(curFov, camFov, k);

        shake = Mathf.MoveTowards(shake, 0f, raw * 1.5f);
        Vector3 offset = UnityEngine.Random.insideUnitSphere * shake * 0.15f;
        cam.transform.position = curPos + offset;
        cam.transform.LookAt(curLook + offset * 0.5f);
        cam.fieldOfView = curFov;

        dof.focusDistance.value = Vector3.Distance(curPos, curLook);
        dof.aperture.value = Mathf.Lerp(dof.aperture.value, dofAperture, raw * 2f);
    }
}
