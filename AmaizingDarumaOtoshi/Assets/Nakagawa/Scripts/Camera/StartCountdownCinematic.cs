using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // 試合開始のカウントダウン(DarumaMatch.IsCountingDown)の間の演出
    //  ・カメラ: 斜めに回り込んだ角度から、プレイヤーが1人ずつ出てくるところに順に寄り、最後にステージ全体の見慣れた位置へ引いて戻る
    //  ・登場: キャラクターを隠しておき、1人ずつ復活と同じ出現演出(RespawnAppearEffect)を出してから見せる
    //  ・時間はカウントダウンの進み(DarumaMatch.CountdownLength - CountdownLeft)で決める(フェードが明けるまでは最初の状態で止まる)
    //  ・カウントダウンが終わると、カメラの角度・寄りは必ず元に戻し、隠したキャラクターも必ず見せる(操作の向きは変わらない)
    //  ・DarumaMatch と同じ GameObject に付ける。シーンに無ければ試合のシーンを読んだときに自動で付く(AutoAttach)
    // MatchCamera(LateUpdate)より先に寄せる位置を決めるため、早めに実行する
    [DefaultExecutionOrder(-20)]
    public class StartCountdownCinematic : MonoBehaviour
    {
        // シーンに無いとき、DarumaMatch に自動で付けるか
        public static bool AutoAttach = true;

        [Header("カメラ")]
        [SerializeField] bool m_useCamera = true;
        [Tooltip("始まったときに足しておく角度(X: 見下ろす角度, Y: 左右の回り込み)。カウントダウンの終わりに向けて 0 へ戻る")]
        [SerializeField] Vector3 m_startAngle = new Vector3(10.0f, -35.0f, 0.0f);
        [Tooltip("登場するプレイヤーに寄るときの視野角")]
        [SerializeField, Range(5.0f, 60.0f)] float m_closeFov = 24.0f;
        [Tooltip("登場するプレイヤーに寄るときの距離(ステージ全体を映す距離に対する割合)")]
        [SerializeField, Range(0.3f, 1.5f)] float m_closeDistance = 0.85f;
        [Tooltip("全員が出る前・出たあとに全体を映すときの視野角(基準の視野角に対する割合)")]
        [SerializeField, Range(0.5f, 1.2f)] float m_wideFovRatio = 0.9f;
        [Tooltip("寄る先を移すなめらかさ(秒)")]
        [SerializeField, Min(0.0f)] float m_focusSmoothTime = 0.22f;
        [Tooltip("カウントダウンのこの割合から、いつものカメラへ戻り始める")]
        [SerializeField, Range(0.3f, 1.0f)] float m_returnStart = 0.72f;
        [Tooltip("寄る点の、足元からの高さ")]
        [SerializeField] float m_focusHeight = 0.9f;

        [Header("登場")]
        [SerializeField] bool m_useAppear = true;
        [Tooltip("最初の人が出始めるまでの時間(秒)")]
        [SerializeField, Min(0.0f)] float m_firstDelay = 0.2f;
        [Tooltip("次の人が出始めるまでの間隔(秒)。人数が多くて間に合わないときは詰める")]
        [SerializeField, Min(0.0f)] float m_interval = 0.45f;
        [Tooltip("光ってから出てくるまでの時間(秒)")]
        [SerializeField, Min(0.05f)] float m_appearTime = 0.45f;

        [Header("ネット対戦")]
        [SerializeField] bool m_playInNetMatch = false;

        class Slot
        {
            public MatchPlayer player;
            public DarumaCharacter character;
            public readonly List<Renderer> hidden = new List<Renderer>();
            public float startTime;
            public bool started, revealed;
        }

        DarumaMatch m_match;
        MatchCamera m_camera;
        RespawnAppearEffect m_effect;
        readonly List<Slot> m_slots = new List<Slot>();
        bool m_running, m_done;
        Vector3 m_originalAngle;
        float m_baseFov, m_baseDistance;
        Vector3 m_focus, m_focusVelocity;
        float m_fov, m_fovVelocity;
        bool m_focusInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachToMatch();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToMatch();

        static void AttachToMatch()
        {
            if (!AutoAttach) return;
            var match = DarumaMatch.Current != null ? DarumaMatch.Current : FindAnyObjectByType<DarumaMatch>();
            if (match == null || FindAnyObjectByType<StartCountdownCinematic>() != null) return;
            match.gameObject.AddComponent<StartCountdownCinematic>();
        }

        private void Start()
        {
            m_match = GetComponent<DarumaMatch>();
            if (m_match == null) m_match = DarumaMatch.Current;
        }

        private void OnDisable() => Finish();

        private void LateUpdate()
        {
            if (m_done || m_match == null) return;
            if (!m_running)
            {
                if (!m_match.IsCountingDown) return;
                if (m_match.IsNetworked && !m_playInNetMatch) { m_done = true; return; }
                Begin();
            }
            if (!m_match.IsCountingDown || m_match.IsFinished)
            {
                Finish();
                return;
            }

            float length = Mathf.Max(0.01f, m_match.CountdownLength);
            float elapsed = Mathf.Clamp(length - m_match.CountdownLeft, 0.0f, length);
            UpdateSlots(elapsed, length);
            if (m_camera != null) UpdateCamera(elapsed, length);
        }

        private void Begin()
        {
            m_running = true;
            m_effect = FindAnyObjectByType<RespawnAppearEffect>();
            var cam = Camera.main;
            m_camera = m_useCamera && cam != null ? cam.GetComponent<MatchCamera>() : null;
            if (m_camera == null && m_useCamera) m_camera = FindAnyObjectByType<MatchCamera>();
            if (m_camera != null)
            {
                var c = m_camera.GetComponent<Camera>();
                m_originalAngle = m_camera.AngleOffset;
                m_baseFov = c.fieldOfView;
                m_baseDistance = DistanceToFloor(m_camera.transform.position, m_camera.transform.rotation);
                m_fov = m_baseFov * m_wideFovRatio;
            }

            m_slots.Clear();
            foreach (var p in m_match.Players)
            {
                if (p == null) continue;
                var slot = new Slot { player = p };
                m_slots.Add(slot);
                if (m_useAppear) Hide(slot, p.Character);
            }

            // 出始める時間(全員がいつものカメラに戻る前に出られるよう、間隔を詰める)
            float length = Mathf.Max(0.01f, m_match.CountdownLength);
            float interval = m_interval;
            if (m_slots.Count > 1)
            {
                float room = length * m_returnStart - m_firstDelay - m_appearTime;
                interval = Mathf.Clamp(room / (m_slots.Count - 1), 0.0f, m_interval);
            }
            for (int i = 0; i < m_slots.Count; i++) m_slots[i].startTime = m_firstDelay + interval * i;
        }

        // 出現演出を出し、時間が来たら見せる。ステージの切り替えなどで体が出し直されたら、出し直した体も隠す
        private void UpdateSlots(float elapsed, float length)
        {
            foreach (var slot in m_slots)
            {
                if (!m_useAppear) continue;
                var character = slot.player != null ? slot.player.Character : null;
                if (character != slot.character)
                {
                    slot.hidden.Clear();
                    slot.character = null;
                    if (!slot.revealed) Hide(slot, character);
                }
                if (slot.character == null) continue;

                if (!slot.started && elapsed >= slot.startTime)
                {
                    slot.started = true;
                    if (m_effect != null) m_effect.Play(slot.character.transform.position, slot.player.Color, m_appearTime);
                }
                if (slot.started && !slot.revealed && elapsed >= slot.startTime + m_appearTime) Reveal(slot);
            }
        }

        private void Hide(Slot slot, DarumaCharacter character)
        {
            slot.character = character;
            if (character == null) return;
            foreach (var r in character.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                slot.hidden.Add(r);
            }
        }

        private void Reveal(Slot slot)
        {
            slot.revealed = true;
            foreach (var r in slot.hidden) if (r != null) r.enabled = true;
            slot.hidden.Clear();
        }

        private void UpdateCamera(float elapsed, float length)
        {
            float progress = elapsed / length;
            // 回り込んだ角度から、カウントダウンの終わりに元の角度へ戻る
            float settle = Smooth(Mathf.InverseLerp(0.0f, 1.0f, progress));
            m_camera.AngleOffset = m_originalAngle + m_startAngle * (1.0f - settle);

            // いま寄る相手: 最後に出始めた人(まだだれも出ていなければ全員の真ん中)
            Slot target = null;
            foreach (var slot in m_slots) if (slot.started && slot.character != null) target = slot;
            Vector3 focus;
            float fov;
            if (target != null)
            {
                focus = target.character.transform.position + Vector3.up * m_focusHeight;
                fov = m_closeFov;
            }
            else
            {
                focus = Center() + Vector3.up * m_focusHeight;
                fov = m_baseFov * m_wideFovRatio;
            }

            float dt = Time.unscaledDeltaTime;
            if (!m_focusInitialized)
            {
                m_focusInitialized = true;
                m_focus = focus;
            }
            m_focus = Vector3.SmoothDamp(m_focus, focus, ref m_focusVelocity, m_focusSmoothTime, Mathf.Infinity, dt);
            m_fov = Mathf.SmoothDamp(m_fov, fov, ref m_fovVelocity, m_focusSmoothTime, Mathf.Infinity, dt);

            // 終わりに向けて、いつものカメラ(全体)へ戻す
            float weight = 1.0f - Smooth(Mathf.InverseLerp(m_returnStart, 1.0f, progress));
            Quaternion rotation = m_camera.BaseRotation;
            Vector3 position = m_focus - rotation * Vector3.forward * (m_baseDistance * Mathf.Lerp(1.0f, m_closeDistance, CloseAmount()));
            m_camera.SetOverride(position, m_fov, weight);
        }

        // いまの視野角がどれだけ寄っているか(0: 全体 〜 1: 寄りきり)
        private float CloseAmount()
        {
            float wide = m_baseFov * m_wideFovRatio;
            return Mathf.Approximately(wide, m_closeFov) ? 0.0f : Mathf.Clamp01(Mathf.InverseLerp(wide, m_closeFov, m_fov));
        }

        private Vector3 Center()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var slot in m_slots)
            {
                if (slot.character == null) continue;
                sum += slot.character.transform.position;
                n++;
            }
            if (n > 0) return sum / n;
            var area = m_match.StageArea;
            return area != null ? area.transform.position : Vector3.zero;
        }

        // 終わり: 全員を見せ、カメラを元に戻す
        private void Finish()
        {
            if (!m_running || m_done) return;
            m_done = true;
            m_running = false;
            foreach (var slot in m_slots) if (!slot.revealed) Reveal(slot);
            if (m_camera != null)
            {
                m_camera.AngleOffset = m_originalAngle;
                m_camera.ClearOverride();
            }
        }

        // カメラの正面の先にある床(高さ0)までの距離(MatchCamera と同じ考え方)
        private static float DistanceToFloor(Vector3 position, Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            if (forward.y < -0.01f) return -position.y / forward.y;
            return 10.0f;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
