using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yurube.Story
{
    // ストーリーモードの台本データ（JSON）。
    // Assets/Yurube/Story/Resources/Story/<章ID>.json を読み込む。コードを変えずに台詞を足せる。
    //  ・speaker が空、または direction=true の行はト書き（小さめの字で出す／イベントだけ起こす）
    //  ・eventId を付けると、その行を表示したときに演出イベントを起こせる（例：doll_shake）
    [Serializable]
    public class StoryLine
    {
        public string speaker;      // 話者ID（speakers の id）。空ならト書き
        public string text;         // 本文
        public bool direction;      // ト書きにする（台詞ではなく演出の指示）
        public string eventId;      // 演出イベントID（例：doll_shake）
        public string portrait;     // 顔アイコン（空なら話者の色の丸で代用）
    }

    [Serializable]
    public class StorySpeaker
    {
        public string id;           // 台本で使う名前（例：akadaru）
        public string name;         // 画面に出す名前（例：あかだる）
        public string color;        // 顔アイコンと名前の色（#RRGGBB）
    }

    // 対戦中のチュートリアル1ステップ
    [Serializable]
    public class TutorialStep
    {
        public string speaker;      // 話者ID（ふつうはみだる）
        public string text;         // 台詞
        // MoveAndJump / Stack / Attack / Reflect / DialogueDone
        public string condition;
        public int count = 1;       // 条件の回数（1でよい）
        // このステップの間だけ動かすCPU（キャラID。空なら全員見学、"all" なら全員）
        public string cpu;
    }

    // 章の対戦設定（CPU1人分）
    [Serializable]
    public class StoryCpu
    {
        public string character;    // キャラID（例：akadaru）
        public int level = 1;       // CPUの強さ（1〜3）
        public int stocks = 1;      // その人だけストックを変えたいとき（0なら章の設定）
        // 画面の名札だけ別名にする（例：巌高校 選手A）。空ならキャラの名前
        public string displayName;
    }

    // 章の対戦ルール
    [Serializable]
    public class StoryRules
    {
        public string mode = "Stock";   // Stock / Time / Ghost
        public int stocks = 1;
        public float timeLimit;         // 0 なら時間制限なし
        public bool cpuSpecial = true;  // CPU の必殺技を使ってよいか（チュートリアルでは false 推奨）
        // 必殺技の覚醒イベントを出す章か（序章だけ true）
        public bool awakening;
        // 必殺技ゲージを最初から見せるか。見せ場まで隠す章は false（序章・第1章）
        public bool gaugeFromStart = true;
        // チュートリアル用の難易度（0 なら通常）。CPU だけに効く
        public int cpuBlockLimit;
    }

    // 対戦中の「追い詰められたら撃つ」イベント（第2章以降）。
    // 章データで有効／無効・トリガー・台詞・プロンプトを変えられる。
    // 序章の覚醒イベント（rules.awakening）は「必ず使う」仕様なので、こちらとは別に扱う。
    [Serializable]
    public class StorySpecialEvent
    {
        public bool enabled;
        // lowStock: ストックが stockThreshold 以下になったら / time: timeSeconds 経過したら
        // （timeFallback が true なら、lowStock の保険として時間でも起こす）
        public string trigger = "lowStock";
        public int stockThreshold = 1;
        // 時間経過の保険（既定は無効。Inspector の StorySettings でも有効にできる）
        public bool timeFallback;
        public float timeSeconds = 90f;
        // 掛け合いのあいだ試合を止めるか
        public bool pause = true;
        // 操作プロンプト（既存の表記に合わせる。キーボード L／パッド Y）
        public string prompt = "[L／Y]で必殺技！";
        // プロンプトを出しておく秒数。必殺技を使えば早く消える
        public float promptSeconds = 10f;
        // リトライ後に、もう一度ストック1になったときの補助（ゲージ満タン＋READY＋プロンプト）を出すか
        public bool replayAssist = true;
        // 掛け合い（初回だけ。対戦中の小窓で約3秒ずつ）
        public List<StoryLine> lines = new List<StoryLine>();
    }

    // カットシーンの1カット（章データで並びと長さを決める）
    [Serializable]
    public class StoryShot
    {
        // wide: 会場の全景 / runner: 走っている人 / rival: 相手
        public string view = "wide";
        public float seconds = 3.0f;
    }

    // 章のカットシーン。type が空なら序章の部室ムービー（StoryMovie.PlayOpening）を使う
    [Serializable]
    public class StoryCutscene
    {
        public string type;             // "stage" = 実際のステージを撮る
        public string caption;          // 場所テロップ（例：夏 ― 地区大会 決勝）
        public string runner;           // 走るキャラID（空なら playerCharacter）
        public string rival;            // 相手のキャラID（空なら最初のCPU）
        public List<StoryShot> shots = new List<StoryShot>();
    }

    // 新聞（1枚絵）
    [Serializable]
    public class StoryNewspaper
    {
        public string header = "だるま新聞";
        public string headline;         // 見出し
        public string body;             // 本文（空でもよい）
        public string image;            // 画像（Resources のパス。空なら新聞用の絵を作る）
        public string caption;          // 写真のキャプション（空なら出さない）
    }

    // 章1つ分
    [Serializable]
    public class StoryChapter
    {
        public string id;
        public string title;            // 章の名前（例：序章「はじまりは、おにぎり」）
        // 章選択の並び順（小さいほど下の段）と、解放に必要な章ID
        public int order;
        public string requiredChapter;  // 空なら最初から遊べる
        public string stageId;          // StageInfo の Id（例：DebugStage31）
        public string sceneName = "AllStages";
        public string playerCharacter = "kidaru";
        public string playerDevice;     // 空なら ゲームパッド→キーボード の順で自動
        public string clearKey;         // 章クリアの保存キー（例：Story.Clear.prologue）
        public StoryRules rules = new StoryRules();
        public List<StoryCpu> cpu = new List<StoryCpu>();
        public List<StorySpeaker> speakers = new List<StorySpeaker>();
        public List<StoryLine> before = new List<StoryLine>();
        public List<StoryLine> after = new List<StoryLine>();
        public List<StoryLine> defeat = new List<StoryLine>();
        public List<TutorialStep> tutorial = new List<TutorialStep>();
        // チュートリアルのあと、試合を始める前の一言
        public List<StoryLine> matchStart = new List<StoryLine>();
        // 試合開始の小窓に出す一言（空なら「練習試合、開始！」）
        public string matchStartNotice;
        // 初回だけ出すイベントの台詞（打ち返し成功・いわの積み木が壁で砕けた）
        public List<StoryLine> reflectFirst = new List<StoryLine>();
        public List<StoryLine> wallBreakFirst = new List<StoryLine>();
        // 初回だけ出す連鎖の台詞（相手に当てたあと、抜けた積み木が別の積み木に当たった）
        public List<StoryLine> chainFirst = new List<StoryLine>();
        // 連鎖の詰まり防止で、一定時間たっても起きないときに出すヒント
        public List<StoryLine> chainHint = new List<StoryLine>();
        // カットシーン（type が空なら序章の部室ムービー）
        public StoryCutscene cutscene = new StoryCutscene();
        // 必殺技の覚醒イベント（初回だけ。対戦中の小窓で出す）
        public List<StoryLine> awakening = new List<StoryLine>();
        // 「追い詰められたら撃つ」必殺技イベント（第2章以降。初回だけ）
        public StorySpecialEvent specialEvent = new StorySpecialEvent();
        // 対戦中に一度だけ出す掛け合い（CPUが攻撃したときなど）
        public List<StoryLine> cpuTaunts = new List<StoryLine>();
        // CPU の掛け合いを「最初のショット」で出すか（false なら最初にダメージを与えたとき）
        public bool cpuTauntOnShot;
        // 初回だけ出す掛け合い（黄色い積み木を積んで増えたとき）
        public List<StoryLine> yellowExtra = new List<StoryLine>();
        public StoryNewspaper newspaper = new StoryNewspaper();
    }

    // 章データの読み込みと、話者・色の取り出し
    public static class StoryData
    {
        public const string ResourceFolder = "Story/";

        // 章IDから JSON を読む（無ければ null）
        public static StoryChapter Load(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return null;
            var asset = Resources.Load<TextAsset>(ResourceFolder + chapterId);
            if (asset == null)
            {
                Debug.LogError("[Story] 台本が見つかりません: Resources/" + ResourceFolder + chapterId + ".json");
                return null;
            }
            var chapter = JsonUtility.FromJson<StoryChapter>(asset.text);
            if (chapter == null)
            {
                Debug.LogError("[Story] 台本を読めませんでした: " + chapterId);
                return null;
            }
            chapter.id = chapterId;
            return chapter;
        }

        // Resources/Story にある章を全部読む（章選択用。order の小さい順）
        public static List<StoryChapter> LoadAll()
        {
            var list = new List<StoryChapter>();
            var assets = Resources.LoadAll<TextAsset>("Story");
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset == null || string.IsNullOrEmpty(asset.text)) continue;
                    StoryChapter chapter = null;
                    try
                    {
                        chapter = JsonUtility.FromJson<StoryChapter>(asset.text);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Story] 台本を読めませんでした: " + asset.name + " / " + e.Message);
                    }
                    if (chapter == null) continue;
                    if (string.IsNullOrEmpty(chapter.id)) chapter.id = asset.name;
                    list.Add(chapter);
                }
            }
            list.Sort((a, b) =>
            {
                int byOrder = a.order.CompareTo(b.order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.id, b.id);
            });
            return list;
        }

        public static StorySpeaker FindSpeaker(StoryChapter chapter, string id)
        {
            if (chapter == null || string.IsNullOrEmpty(id)) return null;
            foreach (var s in chapter.speakers)
            {
                if (s != null && s.id == id) return s;
            }
            return null;
        }

        public static string SpeakerName(StoryChapter chapter, string id)
        {
            var s = FindSpeaker(chapter, id);
            return s != null ? s.name : id;
        }

        public static Color SpeakerColor(StoryChapter chapter, string id, Color fallback)
        {
            var s = FindSpeaker(chapter, id);
            if (s == null || string.IsNullOrEmpty(s.color)) return fallback;
            return ColorUtility.TryParseHtmlString(s.color, out var c) ? c : fallback;
        }

        // 話者の顔（キャラクターの立ち絵があれば使う。無ければ null）
        public static Sprite Portrait(StoryChapter chapter, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var settings = Yurube.Achievements.AchievementSettings.Instance;
            var roster = settings != null ? settings.Roster : null;
            if (roster == null || roster.characters == null) return null;
            foreach (var c in roster.characters)
            {
                if (c != null && c.id == id) return c.portrait;
            }
            return null;
        }

        // イベントIDの実行（今は人形が揺れる演出だけ。増やすときはここに足す）
        public static event Action<string> EventPlayed;
        public static void PlayEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            EventPlayed?.Invoke(eventId);
            if (eventId == "doll_shake") DollShake();
            else Debug.Log("[Story] イベント: " + eventId);
        }

        // 窓際のだるま人形を揺らす（人形のオブジェクトが無くても音とログだけで動く）
        static void DollShake()
        {
            var doll = GameObject.Find("Doll");
            if (doll != null) doll.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            Debug.Log("[Story] だるま人形が カタ、と揺れた");
        }
    }

    // 画面に出す操作説明。ストーリー内のカットシーン・会話・実績で同じ表記を使う。
    // 割り当てもここを見れば分かるように、文言はこの1か所だけに書く。
    public static class StoryText
    {
        public const string Advance = "A：つぎへ";
        public const string Skip = "B：スキップ";
        public const string Hint = Advance + "　" + Skip;
    }
}
