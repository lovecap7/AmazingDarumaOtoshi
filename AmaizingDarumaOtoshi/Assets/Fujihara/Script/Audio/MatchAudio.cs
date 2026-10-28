using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

// 試合（ゲーム画面）の BGM と効果音（仮）。試合のシーン（DarumaMatch がある）に自動で付く（シーンに置く必要はない）。
// 試合や部品のお知らせ（イベント）を聞いて鳴らすだけなので、試合の進み方には影響しない。
//   BGM　　：試合が始まったら流し、残り10秒で少し速く、試合が終わったら止める
//   ハンマー：振る（薙ぎ払い・股抜きショット）／積み木に当たる／打ち返す（回数で高く）／キャラクターに当たる
//   積み木　：キャラクターに当たる／積み木どうしがぶつかる（速さで大きさを変える）／拾う（積んだ数で高く）
//   キャラ　：ジャンプ／顔を飛ばされた／場外に落ちた／幽霊になった／復活した／脱落した
//   進行　　：開始の笛／残り10秒からのカウント／終了の笛（タイムアップは別の笛）／勝者のファンファーレ・引き分け
//   タイムだけ：点が増えた・減った
// 音は MatchSounds（Resources/MatchSounds.asset）で差し替えられる。音量はメニューのサウンド設定（BGM / SE）に従う。
public class MatchAudio : MonoBehaviour
{
    const int PoolSize = 16;
    const float MinInterval = 0.04f;    // 同じ音を続けて鳴らす間隔の下限（連鎖で鳴りすぎないように）

    // 1人分の見張り（ハンマーを振り始めた・ジャンプした、を見つける）
    class Watch
    {
        public MatchPlayer player;
        public Transform body;
        public DarumaHammer hammer;
        public bool busy, grounded = true;
    }

    DarumaMatch m_match;
    MatchSounds m_sounds;
    AudioSource m_bgm;
    readonly List<AudioSource> m_pool = new List<AudioSource>();
    readonly Dictionary<AudioClip, float> m_lastPlayed = new Dictionary<AudioClip, float>();
    readonly List<Watch> m_watches = new List<Watch>();
    float m_bgmFade = 1f;
    int m_lastTick = -1;
    bool m_finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // シーンが読み込まれるたびに、試合のシーンなら自分を付ける（DarumaMatch.Current は Awake で入るので、Start より前に見つかる）
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var match = DarumaMatch.Current;
        if (match == null || match.gameObject.scene != scene) return;
        if (match.GetComponent<MatchAudio>() == null) match.gameObject.AddComponent<MatchAudio>();
    }

    void Awake()
    {
        m_match = GetComponent<DarumaMatch>();
        // 自動テスト（速さを上げて回す）のときは鳴らさない
        if (m_match == null || DarumaMatch.PlaySpeed > 1.5f) { enabled = false; return; }
        m_sounds = MatchSounds.Instance;

        m_bgm = gameObject.AddComponent<AudioSource>();
        m_bgm.playOnAwake = false;
        m_bgm.loop = true;
        for (int i = 0; i < PoolSize; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            m_pool.Add(s);
        }
        Prewarm();

        // 試合のお知らせ（DarumaMatch の Start より前に聞き始める）
        m_match.Started += OnStarted;
        m_match.PlayerJoined += AddWatch;
        m_match.PlayerRespawned += p => Play(m_sounds.Respawn, 0.8f, 1f, p.Body);
        m_match.PlayerOut += p => Play(m_sounds.PlayerOut, 0.8f);
        m_match.Scored += OnScored;
        m_match.Finished += OnFinished;
        // 部品のお知らせ（全キャラクター・全積み木で共通）
        DarumaHammer.Hit += OnHammerHit;
        TumikiBlock.Hit += OnBlockHit;
        DarumaCharacter.AnyEliminated += OnEliminated;
        DarumaCharacter.AnyPickedUp += OnPickedUp;
        GameSettings.AudioChanged += ApplyBgmVolume;
    }

    void OnDestroy()
    {
        DarumaHammer.Hit -= OnHammerHit;
        TumikiBlock.Hit -= OnBlockHit;
        DarumaCharacter.AnyEliminated -= OnEliminated;
        DarumaCharacter.AnyPickedUp -= OnPickedUp;
        GameSettings.AudioChanged -= ApplyBgmVolume;
    }

    // 仮の音は最初に使うときに作るので、試合が始まってから止まらないように、読み込みのうちに作っておく
    void Prewarm()
    {
        var s = m_sounds;
        foreach (var clip in new[] { s.Bgm, s.Swing, s.HitBlock, s.Knock,
                                     s.BlockHitBlock, s.Pickup, s.Jump, s.KO, s.Fall, s.Ghost, s.Respawn, s.PlayerOut, s.ScoreUp,
                                     s.ScoreDown, s.StartWhistle, s.Tick, s.TickLast, s.FinishWhistle, s.TimeUpWhistle, s.Victory, s.Draw })
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
    }

    // ================= 試合の進行 =================
    void OnStarted()
    {
        foreach (var p in m_match.Players) AddWatch(p);
        Play(m_sounds.StartWhistle, 0.7f);
        m_bgm.clip = m_sounds.Bgm;
        m_bgmFade = 1f;
        ApplyBgmVolume();
        m_bgm.Play();
    }

    void AddWatch(MatchPlayer p)
    {
        if (p == null || m_watches.Exists(w => w.player == p)) return;
        m_watches.Add(new Watch { player = p });
        p.StateChanged += () =>
        {
            if (p.State == MatchPlayer.PlayerState.Ghost) StartCoroutine(PlayLater(m_sounds.Ghost, 0.25f, 0.7f, p.Body));
        };
    }

    void OnScored(MatchPlayer p, int points, DarumaMatch.ScoreReason reason)
    {
        if (points > 0) Play(m_sounds.ScoreUp, 0.45f, 1f, p.Body);
        else if (reason != DarumaMatch.ScoreReason.SelfOut) Play(m_sounds.ScoreDown, 0.3f, 1f, p.Body);   // 自分から落ちたときは落ちる音だけ
    }

    void OnFinished(DarumaMatch.MatchResult result)
    {
        m_finished = true;
        StartCoroutine(FinishRoutine(result));
    }

    IEnumerator FinishRoutine(DarumaMatch.MatchResult result)
    {
        bool timeUp = result.Reason == DarumaMatch.FinishReason.TimeUp;
        Play(timeUp ? m_sounds.TimeUpWhistle : m_sounds.FinishWhistle, 0.8f);
        // 試合が終わると Time.timeScale = 0 になるので、実時間で進める
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            m_bgmFade = 1f - t / 0.5f;
            ApplyBgmVolume();
            yield return null;
        }
        m_bgm.Stop();
        yield return new WaitForSecondsRealtime(timeUp ? 0.5f : 0.4f);
        PlayAcrossScenes(result.Winner != null ? m_sounds.Victory : m_sounds.Draw, 0.8f);
    }

    // リザルト画面へ移っても途中で切れないように、シーンをまたいで鳴らす（鳴り終わったら消える）
    void PlayAcrossScenes(AudioClip clip, float volume)
    {
        if (clip == null) return;
        var go = new GameObject("MatchJingle");
        DontDestroyOnLoad(go);
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume * m_sounds.SeVolume * GameSettings.SeVolume;
        source.Play();
        Destroy(go, clip.length + 0.1f);
    }

    void Update()
    {
        if (m_finished) return;
        UpdateWatches();

        // 残り10秒から1秒ごとにカウント（3秒からは高い音）。BGM も少し急がせる
        if (m_match.HasTimeLimit)
        {
            float left = m_match.TimeLeft;
            int sec = Mathf.CeilToInt(left);
            if (sec >= 1 && sec <= 10 && sec != m_lastTick)
            {
                m_lastTick = sec;
                Play(sec <= 3 ? m_sounds.TickLast : m_sounds.Tick, 0.6f);
            }
            m_bgm.pitch = left <= 10f ? m_sounds.HurryPitch : 1f;
        }
    }

    // ハンマーを振り始めた・ジャンプした（お知らせが無いので、毎フレーム様子を見る）
    void UpdateWatches()
    {
        foreach (var w in m_watches)
        {
            var body = w.player.Body;
            if (body != w.body)
            {
                // 体が変わった（復活・幽霊になった）
                w.body = body;
                w.hammer = body != null ? body.GetComponentInChildren<DarumaHammer>() : null;
                w.busy = w.hammer != null && w.hammer.IsBusy;
                w.grounded = true;
            }
            if (body == null) continue;

            if (w.hammer != null)
            {
                bool busy = w.hammer.IsBusy;
                if (busy && !w.busy)
                {
                    // 振った音（股抜きショットは少し低く重く、幽霊は小さく低く）
                    bool ghost = w.player.State == MatchPlayer.PlayerState.Ghost;
                    if (w.hammer.IsShooting) Play(m_sounds.Swing, 0.8f, 0.85f, body);
                    else Play(m_sounds.Swing, ghost ? 0.45f : 0.7f, ghost ? 0.8f : 1f, body);
                }
                w.busy = busy;
            }

            var c = w.player.Character;
            if (c != null)
            {
                bool grounded = c.IsGrounded;
                if (!grounded && w.grounded && c.Rigidbody != null && c.Rigidbody.linearVelocity.y > 1f) Play(m_sounds.Jump, 0.5f, 1f, body);
                w.grounded = grounded;
            }
        }
    }

    // ================= 部品のお知らせ =================
    void OnHammerHit(HammerHitInfo h)
    {
        if (h.Target is DarumaCharacter)
        {
            // ハンマーで相手の積み木を直接はじき出した → 積み木を飛ばした音。無敵中で効かなかったときは軽い音
            if (h.Damaged) Play(m_sounds.Knock, 1f, 1f, h.Point);
            else Play(m_sounds.BlockHitBlock, 0.4f, 0.8f, h.Point);
        }
        else
        {
            // 打ち返し：①→②→③…、最後の段からは最後の音を少しずつ高く（打ち返しの音が入っていれば）
            var ret = m_sounds.ReturnHit(h.ReturnCount);
            if (ret != null) { Play(ret, 1f, m_sounds.ReturnPitch(h.ReturnCount), h.Point); return; }
            // 振ったハンマーが積み木に当たった（打ち返しの音が無いときは、打ち返すほど高く）
            float pitch = h.ReturnCount > 0 ? Mathf.Min(1.05f + 0.08f * (h.ReturnCount - 1), 1.5f) : 1f;
            Play(m_sounds.HitBlock, 0.9f, pitch, h.Point);
        }
    }

    void OnBlockHit(TumikiHitInfo h)
    {
        float power = Mathf.Clamp01(h.Speed / 15f);
        if (h.Target is DarumaCharacter)
        {
            // 飛ばした積み木が相手に当たった → 積み木を飛ばした音
            if (h.IsHarmful) Play(m_sounds.Knock, Mathf.Lerp(0.6f, 1f, power), 1f, h.Point);
        }
        else if (h.Speed > 1.5f) Play(m_sounds.BlockHitBlock, Mathf.Lerp(0.2f, 0.8f, power), Random.Range(0.9f, 1.15f), h.Point);
    }

    void OnEliminated(DarumaCharacter c)
    {
        if (c == null) return;
        if (c.EliminationCause == EliminationCause.Fall) Play(m_sounds.Fall, 1f, 1f, c.transform.position);
        else Play(m_sounds.KO, 1f, 1f, c.transform.position);
    }

    void OnPickedUp(TumikiPickupInfo info)
    {
        if (info.Character == null) return;
        // 積むほど高く
        float pitch = Mathf.Min(0.9f + 0.05f * info.Character.Stack.Count, 1.6f);
        Play(m_sounds.Pickup, info.WasHarmful ? 0.9f : 0.65f, pitch, info.Character.transform.position);
    }

    // ================= 鳴らす =================
    IEnumerator PlayLater(AudioClip clip, float delay, float volume, Transform at)
    {
        yield return new WaitForSecondsRealtime(delay);
        Play(clip, volume, 1f, at);
    }

    void Play(AudioClip clip, float volume, float pitch = 1f, Transform at = null)
    {
        Play(clip, volume, pitch, at != null ? at.position : (Vector3?)null);
    }

    void Play(AudioClip clip, float volume, float pitch, Vector3? at)
    {
        if (clip == null || !enabled) return;
        float now = Time.unscaledTime;
        if (m_lastPlayed.TryGetValue(clip, out float last) && now - last < MinInterval) return;
        m_lastPlayed[clip] = now;

        // 鳴っていない AudioSource を使う（全部鳴っていたら、一番古いものを止めて使う）
        AudioSource source = m_pool.Find(s => !s.isPlaying);
        if (source == null)
        {
            source = m_pool[0];
            m_pool.RemoveAt(0);
            m_pool.Add(source);
        }
        source.clip = clip;
        source.volume = volume * m_sounds.SeVolume * GameSettings.SeVolume;
        source.pitch = pitch * Random.Range(0.97f, 1.03f);   // 同じ音が続いても単調にならないように
        source.panStereo = at.HasValue ? Pan(at.Value) : 0f;
        source.Play();
    }

    // 画面の左右の位置で、音を左右に振る
    static float Pan(Vector3 world)
    {
        var cam = Camera.main;
        if (cam == null) return 0f;
        float x = cam.WorldToViewportPoint(world).x;
        return Mathf.Clamp((x - 0.5f) * 1.2f, -0.7f, 0.7f);
    }

    void ApplyBgmVolume()
    {
        if (m_bgm != null) m_bgm.volume = m_sounds.BgmVolume * GameSettings.BgmVolume * m_bgmFade;
    }
}
