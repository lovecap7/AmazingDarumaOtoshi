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
        public string condition;    // MoveAndJump / Stack / Attack / DialogueDone
        public int count = 1;       // 条件の回数（1でよい）
    }

    // 章の対戦設定（CPU1人分）
    [Serializable]
    public class StoryCpu
    {
        public string character;    // キャラID（例：akadaru）
        public int level = 1;       // CPUの強さ（1〜3）
        public int stocks = 1;      // その人だけストックを変えたいとき（0なら章の設定）
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
        // 必殺技ゲージを最初から見せるか。序章は false（覚醒の瞬間まで隠す）
        public bool gaugeFromStart = true;
        // チュートリアル用の難易度（0 なら通常）。CPU だけに効く
        public int cpuBlockLimit;
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
        // 必殺技の覚醒イベント（初回だけ。対戦中の小窓で出す）
        public List<StoryLine> awakening = new List<StoryLine>();
        // 対戦中に一度だけ出す掛け合い（CPUが攻撃したときなど）
        public List<StoryLine> cpuTaunts = new List<StoryLine>();
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
