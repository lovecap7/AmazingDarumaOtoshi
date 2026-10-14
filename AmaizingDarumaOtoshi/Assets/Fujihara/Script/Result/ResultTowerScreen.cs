using System;
using System.Collections;
using System.Collections.Generic;
using Nakahira;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// リザルト画面（1位の表彰）その2。「積み木タワーの高さ自慢」の演出。
//  1. 1位の顔はタワーの横で待っている。カメラは足元から見上げる
//  2. 1位が試合の最後に持っていた積み木（同じ色・同じ数）が、1個ずつ落ちてきて床から積み上がる
//     カメラは積み上がるてっぺんを追って、下から上へパンしていく。だんだん速く
//  3. 顔が「ぴょーん」とてっぺんへ跳び乗る（ポテッ）
//  4. その頭に王冠が「ポンッ」とかぶさる（キラキラ・紙吹雪）
//  5. カメラが引いてタワー全体を見せ、「たかさ ○こ！」「1P のかち！」。2位以下は足元から見上げている
// 引き分けのときは、1位の人たちのタワーが並んで同時に積み上がり、それぞれ跳び乗る（王冠なし）。
// 表彰台の演出（ResultScreen / ResultScene）とは別の画面。使う方を MatchSettings の「リザルト画面のシーン名」で選ぶ。
// 試合の結果は MatchResultRelay が ResultData に入れておく。このシーンだけを再生したときは確認用の仮の結果で動く。
public class ResultTowerScreen : MonoBehaviour
{
    [Header("素材")]
    [Tooltip("文字のフォント（未設定なら PresentationSettings のフォント）")]
    [SerializeField] TMP_FontAsset font;

    [Header("シーン")]
    [Tooltip("決定ボタンで移るシーン")]
    [SerializeField] string nextSceneName = "SelectScene";

    [Header("タワー")]
    [Tooltip("積み木が落ちてくる間隔（最初 → 最後。だんだん速くなる）")]
    [SerializeField] float firstInterval = 0.45f;
    [SerializeField] float lastInterval = 0.16f;
    [Tooltip("引き分けのとき、タワーどうしの間隔")]
    [SerializeField] float towerSpacing = 3f;

    [Header("文字")]
    [SerializeField] string winText = "のかち！";
    [SerializeField] string drawText = "ひきわけ！";

    [Header("演出")]
    [Tooltip("「トン」「ポンッ！」などの擬音（字幕）をこの画面で出す")]
    [SerializeField] bool showOnomatopoeia = true;
    [Tooltip("演出中に決定ボタンを押したときの早送りの速さ")]
    [SerializeField] float skipSpeed = 4f;
    [Tooltip("演出が終わってから、この秒数操作がないと自動で次へ（0で無効）")]
    [SerializeField] float autoReturnDelay = 30f;

    [Header("確認用（このシーンだけを再生したとき）")]
    [SerializeField] SelectRoster previewRoster;
    [SerializeField, Range(1, 4)] int previewPlayers = 4;
    [SerializeField, Range(0, 10)] int previewBlocks = 6;
    [SerializeField] bool previewDraw = false;

    const float BlockW = ToyKit.BlockW, BlockH = ToyKit.BlockH, BlockD = ToyKit.BlockD;
    const float HeadR = ToyKit.HeadR;
    static Color[] Palette => ToyKit.Palette;
    static readonly Color Gold = new Color(1f, 0.78f, 0.2f);

    // 1人分のタワー
    class Tower
    {
        public ResultData.Entry entry;
        public Transform root;                              // 足元が原点
        public ToyKit.Face face;
        public TextMeshPro tag;
        public readonly List<Transform> blocks = new List<Transform>();
        public Transform crown;
        public int stacked;                                 // 積み上がった数
        public float wobble;                                // 積んだときの揺れ
        public bool onTop;                                  // 顔がてっぺんに乗ったか
        public float Top => stacked * BlockH;               // 積み木のてっぺんの高さ
        public float HeadTop => Top + HeadR * 2f;           // 顔を乗せたときの頭のてっぺん
    }

    const float WaitSide = -1.5f;   // 顔が待っている位置（タワーの横）
    const float CrownScale = 0.62f; // 頭にかぶせる王冠の大きさ

    // ---- 結果 ----
    readonly List<ResultData.Entry> entries = new List<ResultData.Entry>();
    bool isDraw;

    // ---- 舞台 ----
    Camera cam;
    DepthOfField dof;
    Mesh blockMesh;
    Material letterMat, popMat;
    ParticleSystem confetti, puff, sparkle;
    readonly List<Tower> towers = new List<Tower>();      // 1位（引き分けなら1位の人たち）
    readonly List<Tower> others = new List<Tower>();      // 2位以下（足元で見上げる）
    AudioSource[] audioSources;
    int nextSource;

    // ---- UI ----
    RectTransform uiRoot;
    TextMeshProUGUI titleText, countText, resultText, statsText;
    CanvasGroup titleGroup, countGroup, resultGroup, hintGroup;
    readonly List<GameObject> pops = new List<GameObject>();
    float countPunch;

    // ---- 動き ----
    float baseSpeed = 1f;
    Vector3 camPos, camLook, curPos, curLook;
    float camFov = 45f, curFov = 45f, dofAperture = 4f;
    float shake;
    bool happy, done, leaving;
    float doneTime;

    float Dt => Time.unscaledDeltaTime * baseSpeed;

    // ================= 始まり =================
    void Start()
    {
        // 試合の終わりで止まった時間と、MatchResultRelay が止めた「セレクトへ戻る」を元に戻す
        Time.timeScale = 1f;
        DarumaMatch.AutoReturnToSelect = true;

        if (font == null) font = PresentationSettings.DefaultFont;
        LoadResult();
        BuildStage();
        BuildTowers();
        BuildUI();
        StartCoroutine(Ceremony());
    }

    void LoadResult()
    {
        if (ResultData.HasResult)
        {
            entries.AddRange(ResultData.Ranking);
            isDraw = ResultData.IsDraw;
            ResultData.Clear();
            return;
        }

        // 確認用の仮の結果
        string[] labels = { "1P", "2P", "CPU1", "CPU2" };
        Color[] blockColors = { new Color(0.9f, 0.25f, 0.2f), new Color(0.2f, 0.45f, 0.9f), new Color(0.3f, 0.75f, 0.3f), new Color(0.95f, 0.8f, 0.2f) };
        for (int i = 0; i < previewPlayers; i++)
        {
            var e = new ResultData.Entry
            {
                label = labels[i],
                playerColor = Palette[(i * 2) % Palette.Length],
                character = previewRoster != null && previewRoster.characters.Count > 0 ? previewRoster.characters[i % previewRoster.characters.Count] : null,
                rank = previewDraw && i < 2 ? 1 : i + 1,
                knocks = 6 - i,
                kos = 3 - i / 2,
            };
            int n = i == 0 || (previewDraw && i == 1) ? previewBlocks - i : 0;
            for (int b = 0; b < n; b++) e.blockColors.Add(blockColors[UnityEngine.Random.Range(0, blockColors.Length)]);
            e.blocks = e.blockColors.Count;
            entries.Add(e);
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

        var fx = ToyKit.CreateFx();
        confetti = fx.confetti; puff = fx.puff; sparkle = fx.sparkle;

        audioSources = new AudioSource[8];
        for (int i = 0; i < audioSources.Length; i++)
        {
            audioSources[i] = gameObject.AddComponent<AudioSource>();
            audioSources[i].playOnAwake = false;
        }
    }

    void BuildTowers()
    {
        var tops = entries.FindAll(e => e.rank == 1);
        if (!isDraw && tops.Count > 1) tops.RemoveRange(1, tops.Count - 1);
        for (int i = 0; i < tops.Count; i++)
        {
            float x = (i - (tops.Count - 1) * 0.5f) * towerSpacing;
            var t = MakeTower(tops[i], new Vector3(x, 0f, 0f));
            t.face.root.localPosition = new Vector3(WaitSide, HeadR, 0f);   // 積み終わるまで横で待つ
            // 試合の最後に持っていた積み木（同じ色）。最初は隠しておき、1個ずつ落として床から積む
            for (int b = 0; b < tops[i].blockColors.Count; b++)
            {
                var blk = ToyKit.CreateBlock("", tops[i].blockColors[b], blockMesh, new Vector2(1.05f, 0.68f), BlockD, font, letterMat, out _);
                blk.SetParent(t.root, false);
                blk.localPosition = new Vector3(0f, (b + 0.5f) * BlockH, 0f);
                blk.gameObject.SetActive(false);
                t.blocks.Add(blk);
            }
            if (!isDraw)
            {
                t.crown = MakeCrown(t.root);
                t.crown.gameObject.SetActive(false);
            }
            towers.Add(t);
        }

        // 2位以下は足元に並んで見上げる
        int k = 0;
        foreach (var e in entries)
        {
            if (tops.Contains(e)) continue;
            float side = (k % 2 == 0 ? -1f : 1f) * (3.3f + (k / 2) * 1.5f);   // 1位が横で待つ場所と重ならないように
            float spread = (towers.Count - 1) * towerSpacing * 0.5f;
            var t = MakeTower(e, new Vector3(side + Mathf.Sign(side) * spread, 0f, 1.2f + (k / 2) * 0.6f));
            t.root.localScale = Vector3.one * 0.85f;
            t.tag.gameObject.SetActive(false);
            others.Add(t);
            k++;
        }
    }

    Tower MakeTower(ResultData.Entry e, Vector3 at)
    {
        var t = new Tower { entry = e };
        t.root = new GameObject("Tower_" + e.label).transform;
        t.root.position = at;
        t.face = ToyKit.CreateHead();
        t.face.root.SetParent(t.root, false);
        t.face.root.localPosition = new Vector3(0f, HeadR, 0f);
        if (e.character != null && e.character.color != Color.white) t.face.body.SetColor("_BaseColor", e.character.color);
        t.tag = ToyKit.Text3D(font, letterMat, e.label, t.root, new Vector2(2f, 0.5f), 3f);
        t.tag.color = e.playerColor;
        // 名札は顔の前の床に立てる
        t.tag.transform.localPosition = new Vector3(0f, 0.2f, -HeadR - 0.45f);
        return t;
    }

    // 王冠：金色の台の上に、とがった飾りと宝石（頭にかぶせるので小さめ）
    Transform MakeCrown(Transform parent)
    {
        var root = new GameObject("Crown").transform;
        root.SetParent(parent, false);
        root.localScale = Vector3.one * CrownScale;
        var gold = ToyKit.Lit(Gold, 0.92f);
        gold.EnableKeyword("_EMISSION");
        gold.SetColor("_EmissionColor", Gold * 0.25f);

        var body = new GameObject("Body");
        body.transform.SetParent(root, false);
        body.AddComponent<MeshFilter>().sharedMesh = blockMesh;
        body.AddComponent<MeshRenderer>().sharedMaterial = gold;
        body.transform.localScale = new Vector3(0.9f, 0.7f, 0.9f);

        var red = ToyKit.Lit(new Color(0.9f, 0.15f, 0.2f), 0.95f);
        var blue = ToyKit.Lit(new Color(0.2f, 0.45f, 0.95f), 0.95f);
        float half = BlockW * 0.9f * 0.5f - 0.1f;
        Vector3[] corners =
        {
            new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(-half, 0f, half), new Vector3(half, 0f, half),
            new Vector3(0f, 0f, -half), new Vector3(0f, 0f, half), new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f),
        };
        for (int i = 0; i < corners.Length; i++)
        {
            bool big = i < 4;
            var spike = ToyKit.Primitive(PrimitiveType.Cylinder, "Spike", root);
            spike.localScale = new Vector3(0.14f, big ? 0.22f : 0.15f, 0.14f);
            spike.localPosition = corners[i] + Vector3.up * (BlockH * 0.35f + (big ? 0.22f : 0.15f));
            spike.GetComponent<Renderer>().sharedMaterial = gold;
            var ball = ToyKit.Primitive(PrimitiveType.Sphere, "Ball", root);
            ball.localScale = Vector3.one * (big ? 0.2f : 0.15f);
            ball.localPosition = spike.localPosition + Vector3.up * (big ? 0.24f : 0.17f);
            ball.GetComponent<Renderer>().sharedMaterial = gold;
        }
        // 正面の宝石
        var jewel = ToyKit.Primitive(PrimitiveType.Sphere, "Jewel", root);
        jewel.localScale = new Vector3(0.26f, 0.26f, 0.12f);
        jewel.localPosition = new Vector3(0f, 0f, -BlockD * 0.9f * 0.5f - 0.02f);
        jewel.GetComponent<Renderer>().sharedMaterial = red;
        foreach (float x in new[] { -0.32f, 0.32f })
        {
            var j = ToyKit.Primitive(PrimitiveType.Sphere, "Jewel", root);
            j.localScale = new Vector3(0.14f, 0.14f, 0.08f);
            j.localPosition = new Vector3(x, 0f, -BlockD * 0.9f * 0.5f - 0.01f);
            j.GetComponent<Renderer>().sharedMaterial = blue;
        }
        return root;
    }

    // ---------------- UI ----------------
    void BuildUI()
    {
        uiRoot = ToyKit.CreateOverlayCanvas("UI", 10);

        string who = isDraw ? "" : towers.Count > 0 ? towers[0].entry.label + " の" : "";
        titleText = ToyKit.UIText("Title", uiRoot, font, isDraw ? "タワーくらべ！" : who + "タワー！", 80f, Color.white);
        titleText.fontSharedMaterial = popMat;
        ToyKit.Anchor(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1400f, 140f));
        titleGroup = titleText.gameObject.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;

        // 右側の「たかさ ○こ」
        var countRoot = new GameObject("Count", typeof(RectTransform)).GetComponent<RectTransform>();
        countRoot.SetParent(uiRoot, false);
        ToyKit.Anchor(countRoot, new Vector2(1f, 0.5f), new Vector2(-260f, 0f), new Vector2(400f, 300f));
        countGroup = countRoot.gameObject.AddComponent<CanvasGroup>();
        countGroup.alpha = 0f;
        var label = ToyKit.UIText("Label", countRoot, font, "たかさ", 52f, ToyKit.Ink);
        ToyKit.Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(400f, 70f));
        countText = ToyKit.UIText("Number", countRoot, font, "0こ", 140f, Palette[2]);
        countText.fontSharedMaterial = popMat;
        ToyKit.Anchor(countText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(400f, 180f));

        // 最後の「1P のかち！」と記録
        var res = new GameObject("Result", typeof(RectTransform)).GetComponent<RectTransform>();
        res.SetParent(uiRoot, false);
        ToyKit.Anchor(res, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1400f, 240f));
        resultGroup = res.gameObject.AddComponent<CanvasGroup>();
        resultGroup.alpha = 0f;
        var winner = towers.Count > 0 ? towers[0].entry : null;
        resultText = ToyKit.UIText("Text", res, font, isDraw ? drawText : winner != null ? winner.label + " " + winText : "", 100f, Palette[0]);
        resultText.fontSharedMaterial = popMat;
        ToyKit.Anchor(resultText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1400f, 140f));
        string name = isDraw || winner == null || winner.character == null ? "" : winner.character.displayName + "　　";
        string stats = isDraw || winner == null ? "" : $"{name}くずした　{winner.knocks}　　とばした　{winner.kos}";
        statsText = ToyKit.UIText("Stats", res, font, stats, 38f, ToyKit.Ink);
        ToyKit.Anchor(statsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(1400f, 60f));

        var hint = ToyKit.UIText("Hint", uiRoot, font, "A：つぎへ", 32f, ToyKit.Ink);
        ToyKit.Anchor(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-70f, 40f), new Vector2(500f, 50f));
        hint.rectTransform.pivot = new Vector2(1f, 0f);
        hint.alignment = TextAlignmentOptions.Right;
        hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;
    }

    // ================= 演出の流れ =================
    IEnumerator Ceremony()
    {
        float centerX = 0f;
        float spread = (towers.Count - 1) * towerSpacing;

        // ---- 1. 足元から見上げる（顔は横で待っている） ----
        SetCam(new Vector3(centerX - 1.2f, 0.35f, -4.6f - spread * 0.6f), new Vector3(centerX - 0.5f, 1.1f, 0f), 45f, true);
        yield return Wait(0.5f);
        titleGroup.alpha = 1f;
        StartCoroutine(PunchUI(titleText.rectTransform));
        Play(SoundSynth.Pickup, 0.6f, 0.8f);
        yield return Wait(0.7f);
        if (!isDraw) countGroup.alpha = 1f;   // 引き分けはタワーの高さがちがうことがあるので出さない

        // ---- 2. 積み木が1個ずつ床から積み上がる（カメラは下から上へ） ----
        int max = 0;
        foreach (var t in towers) max = Mathf.Max(max, t.blocks.Count);
        for (int b = 0; b < max; b++)
        {
            float top = (b + 1) * BlockH;
            SetCam(new Vector3(centerX - 0.7f, top - 0.9f, -4.6f - top * 0.12f - spread * 0.6f), new Vector3(centerX, top + 0.1f, 0f), 45f, false);
            foreach (var t in towers)
                if (b < t.blocks.Count) StartCoroutine(DropBlock(t, b));
            float interval = Mathf.Lerp(firstInterval, lastInterval, max <= 1 ? 1f : b / (float)(max - 1));
            yield return Wait(interval);
        }
        yield return Wait(0.35f);

        // ---- 3. 顔がてっぺんへ跳び乗る ----
        float maxTop = 0f;
        foreach (var t in towers) maxTop = Mathf.Max(maxTop, t.HeadTop);
        SetCam(new Vector3(centerX - 1.3f, maxTop + 0.2f, -4.8f - spread * 0.6f), new Vector3(centerX, maxTop - 0.3f, 0f), 44f, false);
        yield return Wait(0.3f);
        for (int i = 0; i < towers.Count; i++)
        {
            if (i < towers.Count - 1) StartCoroutine(HopOnTop(towers[i]));
            else yield return HopOnTop(towers[i]);
        }
        yield return Wait(0.35f);

        if (isDraw)
        {
            yield return Finale();
            yield break;
        }

        // ---- 4. 頭に王冠が「ポンッ」とかぶさる ----
        var w = towers[0];
        float crownY = w.HeadTop - 0.06f + BlockH * 0.35f * CrownScale;
        SetCam(new Vector3(-1.1f, crownY + 0.2f, -4.2f), new Vector3(0f, crownY - 0.1f, 0f), 42f, false);
        yield return Wait(0.45f);   // ため
        yield return DropCrown(w, crownY);
        yield return Wait(0.6f);

        // ---- 5. 引いて全体を見せる ----
        yield return Finale();
    }

    // 顔が横から「ぴょーん」と跳んで、タワーのてっぺんに乗る（ポテッ）
    IEnumerator HopOnTop(Tower t)
    {
        var head = t.face.root;
        Vector3 from = head.localPosition;
        Vector3 to = new Vector3(0f, t.Top + HeadR, 0f);
        const float arc = 1.2f;   // てっぺんより少し上まで跳ぶ
        Play(SoundSynth.Jump, 0.8f, 1f);
        t.tag.gameObject.SetActive(false);   // 足元の名札はタワーに隠れるので消す（名前は画面の文字で出る）
        // 跳ぶ前に、ぐっとかがむ
        for (float u = 0f; u < 1f; u += Dt / 0.18f)
        {
            head.localScale = ToyKit.SquashVec(Mathf.Sin(u * Mathf.PI) * 0.25f);
            yield return null;
        }
        head.localScale = Vector3.one;
        const float time = 0.5f;
        for (float u = 0f; u < 1f; u += Dt / time)
        {
            // 先に高く跳び上がってから横へ移る（タワーの横腹にめり込まないように）
            float rise = 1f - (1f - u) * (1f - u);
            float across = u * u;
            head.localPosition = new Vector3(
                Mathf.Lerp(from.x, to.x, across),
                from.y + (to.y - from.y) * rise + Mathf.Sin(u * Mathf.PI) * arc,
                Mathf.Lerp(from.z, to.z, across));
            head.localRotation = Quaternion.Euler(0f, 0f, -360f * u);   // くるっと1回転
            yield return null;
        }
        head.localPosition = to;
        head.localRotation = Quaternion.identity;
        t.onTop = true;
        t.wobble = 1.2f;

        Vector3 at = t.root.TransformPoint(to);
        Pop("ポテッ", at + new Vector3(1.1f, 0.3f, 0f), 64f, Palette[2], 12f);
        ToyKit.Emit(puff, at - Vector3.up * HeadR, Quaternion.Euler(-90f, 0f, 0f), 8);
        shake = Mathf.Max(shake, 0.12f);
        Play(SoundSynth.Pickup, 0.9f, 0.7f);
        for (float u = 0f; u < 1f; u += Dt / 0.3f)
        {
            head.localScale = ToyKit.SquashVec(ToyKit.Squash(u, 0.2f));
            yield return null;
        }
        head.localScale = Vector3.one;
    }

    IEnumerator Finale()
    {
        float height = 0f;
        foreach (var t in towers) height = Mathf.Max(height, t.HeadTop + (t.crown != null ? 0.7f : 0f));   // 王冠は飾りの先まで
        float spread = (towers.Count - 1) * towerSpacing;
        float dist = Mathf.Max(9f, height * 1.5f + 3.5f) + spread * 0.7f;
        // 下に文字を出すので、タワーは画面の少し上寄りに
        float lookY = height * 0.5f - 0.6f;
        SetCam(new Vector3(0f, lookY + 0.7f, -dist), new Vector3(0f, lookY, 0f), 42f, false);
        dofAperture = 10f;
        happy = true;
        foreach (var t in towers) t.face.SetHappy(true);

        ToyKit.Emit(confetti, new Vector3(-5f, 0.3f, 0.5f), Quaternion.LookRotation(new Vector3(0.45f, 1f, 0f)), 120);
        ToyKit.Emit(confetti, new Vector3(5f, 0.3f, 0.5f), Quaternion.LookRotation(new Vector3(-0.45f, 1f, 0f)), 120);
        StartCoroutine(ConfettiRain(3f, height));
        Play(SoundSynth.ScoreUp, 0.6f, 1f);

        yield return Wait(0.5f);
        resultGroup.alpha = 1f;
        StartCoroutine(PunchUI(resultText.rectTransform));
        shake = 0.25f;

        baseSpeed = 1f;
        done = true;
        doneTime = Time.unscaledTime;
    }

    // 積み木が上から落ちてきて、てっぺんに乗る
    IEnumerator DropBlock(Tower t, int index)
    {
        var blk = t.blocks[index];
        Vector3 local = blk.localPosition;
        blk.gameObject.SetActive(true);
        blk.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(-25f, 25f), 0f);
        const float time = 0.24f, height = 3.2f;
        for (float u = 0f; u < 1f; u += Dt / time)
        {
            blk.localPosition = local + Vector3.up * height * (1f - u * u);
            yield return null;
        }
        blk.localPosition = local;
        blk.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(-4f, 4f), 0f);
        t.stacked = index + 1;
        t.wobble = 1f;

        // 着地：トン（積むほど高い音）・ほこり・数が増える
        Vector3 at = t.root.TransformPoint(local - Vector3.up * BlockH * 0.5f);
        ToyKit.Emit(puff, at, Quaternion.Euler(-90f, 0f, 0f), 4);
        shake = Mathf.Max(shake, 0.05f);
        Play(SoundSynth.Pickup, 0.7f, Mathf.Min(0.85f + 0.06f * index, 1.7f));
        if (index % 2 == 0) Pop("トン", at + new Vector3(1.1f, 0.3f, 0f), 46f, Palette[(index + 2) % Palette.Length], UnityEngine.Random.Range(-12f, 12f));
        if (t == towers[0])
        {
            countText.text = t.stacked + "こ";
            countPunch = 1f;
        }

        Vector3 baseScale = blk.localScale;
        for (float u = 0f; u < 1f; u += Dt / 0.25f)
        {
            blk.localScale = Vector3.Scale(baseScale, ToyKit.SquashVec(ToyKit.Squash(u, 0.15f)));
            yield return null;
        }
        blk.localScale = baseScale;
    }

    // 王冠が回りながら落ちてきて「ポンッ」
    IEnumerator DropCrown(Tower t, float y)
    {
        var c = t.crown;
        Vector3 local = new Vector3(0f, y, 0f);
        c.gameObject.SetActive(true);
        const float time = 0.38f, height = 4.5f;
        for (float u = 0f; u < 1f; u += Dt / time)
        {
            c.localPosition = local + Vector3.up * height * (1f - u * u);
            c.localRotation = Quaternion.Euler(0f, (1f - u) * 540f, 0f);
            yield return null;
        }
        c.localPosition = local;
        c.localRotation = Quaternion.identity;
        t.wobble = 1.4f;

        Vector3 at = t.root.TransformPoint(local);
        Pop("ポンッ！", at + new Vector3(1.3f, 0.7f, 0f), 110f, Gold, -10f, 0.7f);
        ToyKit.Emit(sparkle, at + Vector3.up * 0.3f, Quaternion.Euler(-90f, 0f, 0f), 50);
        ToyKit.Emit(confetti, at + Vector3.up * 0.4f, Quaternion.Euler(-90f, 0f, 0f), 80);
        shake = 0.4f;
        Play(SoundSynth.Pickup, 1f, 1.9f);
        Play(SoundSynth.Respawn, 0.9f, 1f);

        // 王冠も頭も、かぶった勢いでつぶれる
        var head = t.face.root;
        for (float u = 0f; u < 1f; u += Dt / 0.35f)
        {
            var squash = ToyKit.SquashVec(ToyKit.Squash(u, 0.25f));
            c.localScale = squash * CrownScale;
            head.localScale = ToyKit.SquashVec(ToyKit.Squash(u, 0.15f));
            yield return null;
        }
        c.localScale = Vector3.one * CrownScale;
        head.localScale = Vector3.one;
    }

    IEnumerator ConfettiRain(float time, float height)
    {
        for (float t = 0f; t < time; t += 0.2f)
        {
            var at = new Vector3(UnityEngine.Random.Range(-5f, 5f), height + 3f, UnityEngine.Random.Range(-1f, 2f));
            ToyKit.Emit(confetti, at, Quaternion.Euler(90f, 0f, 0f), 18);
            yield return Wait(0.2f);
        }
    }

    IEnumerator PunchUI(RectTransform r)
    {
        for (float u = 0f; u < 1f; u += Time.unscaledDeltaTime / 0.35f)
        {
            r.localScale = Vector3.one * ToyKit.EaseOutBack(u);
            yield return null;
        }
        r.localScale = Vector3.one;
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
        if (!showOnomatopoeia) return;
        var local = ToyKit.WorldToCanvas(cam, uiRoot, world);
        ToyKit.Pop(this, uiRoot, font, popMat, text, local, size, color, angle, hold, pops);
    }

    // 効果音（試合と同じ仮の音。音量はメニューのサウンド設定に従う）
    void Play(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        var source = audioSources[nextSource];
        nextSource = (nextSource + 1) % audioSources.Length;
        source.clip = clip;
        source.pitch = pitch;
        source.volume = volume * MatchSounds.Instance.seVolume * GameSettings.SeVolume;
        source.Play();
    }

    // ================= 毎フレーム =================
    void Update()
    {
        float raw = Time.unscaledDeltaTime;
        HandleInput();
        UpdateTowers(raw);
        UpdateCamera(raw);

        countPunch = Mathf.MoveTowards(countPunch, 0f, raw * 4f);
        countText.rectTransform.localScale = Vector3.one * (1f + 0.35f * countPunch * countPunch);
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, done ? 1f : 0f, raw * 3f);
        if (done) titleGroup.alpha = Mathf.MoveTowards(titleGroup.alpha, 0f, raw * 2f);
    }

    void HandleInput()
    {
        if (leaving) return;
        bool submit = MenuInput.Submit() || MenuInput.AnyClick();
        if (!done)
        {
            if (submit) baseSpeed = skipSpeed;   // 演出中に押したら早送り
            return;
        }
        bool timeout = autoReturnDelay > 0f && Time.unscaledTime - doneTime > autoReturnDelay;
        if ((submit && Time.unscaledTime - doneTime > 0.4f) || timeout)
        {
            leaving = true;
            Fader.Load(nextSceneName);
        }
    }

    void UpdateTowers(float raw)
    {
        float time = Time.unscaledTime;
        foreach (var t in towers)
        {
            // 積んだときにタワーがぐらっと揺れて戻る（上ほど大きく揺れる）
            t.wobble = Mathf.MoveTowards(t.wobble, 0f, raw * 1.6f);
            float sway = Mathf.Sin(time * 14f) * t.wobble * 2.2f + (happy ? Mathf.Sin(time * 2.2f) * 1.2f : 0f);
            for (int i = 0; i < t.blocks.Count; i++)
            {
                if (!t.blocks[i].gameObject.activeSelf) continue;
                float k = (i + 1f) / Mathf.Max(1, t.blocks.Count);
                Vector3 p = t.blocks[i].localPosition;
                if (i < t.stacked) t.blocks[i].localPosition = new Vector3(Mathf.Sin(sway * Mathf.Deg2Rad) * k * 0.6f, p.y, p.z);
            }
            // てっぺんに乗った顔と、かぶった王冠も一緒に揺れる
            float topX = Mathf.Sin(sway * Mathf.Deg2Rad) * 0.6f;
            if (t.onTop) t.face.root.localPosition = new Vector3(topX, t.face.root.localPosition.y, 0f);
            if (t.crown != null && t.crown.gameObject.activeSelf && t.crown.localScale == Vector3.one * CrownScale)
                t.crown.localPosition = new Vector3(topX, t.crown.localPosition.y, 0f);

            // 1位の顔：最後はうれしそうに揺れる
            if (happy) t.face.root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2.5f) * 6f);
        }

        // 2位以下：1位のてっぺんを見上げる
        if (towers.Count > 0)
        {
            var w = towers[0];
            Vector3 top = w.onTop ? w.face.root.position : w.root.position + Vector3.up * (w.Top + 0.5f);
            foreach (var o in others)
            {
                Vector3 dir = o.face.root.position - top;
                var look = Quaternion.LookRotation(dir.normalized);
                o.face.root.rotation = Quaternion.Slerp(o.face.root.rotation, look, 1f - Mathf.Exp(-5f * raw));
            }
        }
    }

    void UpdateCamera(float raw)
    {
        float k = 1f - Mathf.Exp(-3.2f * raw * baseSpeed);
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
