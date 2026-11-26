using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // きだるの必殺技「黄金化」。
    // 発動すると無敵＋移動速度UP。
    // 効果時間は 基本時間 + (発動した瞬間の自分の積み木の数 × 1秒)。
    // 無敵中は既存の点滅を使わず、モデルを虹色に発光させる。
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(DarumaCharacter))]
    public class KidaruSpecial : MonoBehaviour, ISpecialAbility, INetSpecial
    {
        [Header("必殺技")]
        [Tooltip("積み木0個のときの効果時間(秒)")]
        [SerializeField] float m_baseDuration = 3.0f;
        [Tooltip("積み木1個あたり延びる時間(秒)")]
        [SerializeField] float m_durationPerBlock = 1.0f;
        [Tooltip("移動速度の倍率")]
        [SerializeField] float m_speedMultiplier = 1.5f;
        [Tooltip("発動をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        [Header("虹色発光")]
        [Tooltip("虹色が一周する速さ(小さいほどゆっくり)")]
        [SerializeField] float m_rainbowSpeed = 0.5f;
        [Tooltip("発光の強さ")]
        [SerializeField] float m_emissionIntensity = 1.5f;
        [Tooltip("画面下部に表示する必殺技名")]
        [SerializeField] string m_cutInName = "黄金化";

        [Header("震脚(suehara)。オンなら上の「黄金化」の代わりにこちらを出す")]
        [Tooltip("オン: 効果時間の間、ジャンプして着地するたびに周りへ衝撃波を出す / オフ: 前の「黄金化」")]
        [SerializeField] bool m_useQuake = true;
        [Tooltip("技名(カットインに出す)")]
        [SerializeField] string m_quakeName = "震脚";
        [Tooltip("効果時間(秒)")]
        [SerializeField] float m_quakeDuration = 5.0f;
        [Tooltip("衝撃波の半径(m)。この中の敵は空中にいても1段削られる")]
        [SerializeField] float m_quakeRadius = 5.0f;
        [SerializeField] Color m_quakeCutInColor = new Color(1.0f, 0.8f, 0.2f);

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        KidaruWheelVisual m_wheel;
        float m_activeUntil;
        // 震脚: ジャンプして空中にいる(次に着地したら衝撃波)
        bool m_quakeJumping;

        // CPU 用(suehara): 震脚を使うか / 衝撃波の半径
        public bool UsesQuake => m_useQuake;
        public float QuakeRadius => m_quakeRadius;

        Renderer[] m_renderers;
        Material[][] m_originalMaterials;
        Material[][] m_rainbowMaterials;
        bool m_rainbowActive;

        public bool IsActive => Time.time < m_activeUntil;
        public float RemainingSeconds => Mathf.Max(0.0f, m_activeUntil - Time.time);
        public int LastBlocks { get; private set; }
        public float LastDuration { get; private set; }

        // ラボ表示用。実際に速度倍率が掛かっている状態かを返す
        public bool IsSpeedBoosted => IsActive && !m_useQuake && m_character != null && m_character.CanBeAffected;
        public float CurrentSpeedMultiplier => IsSpeedBoosted ? Mathf.Max(1.0f, m_speedMultiplier) : 1.0f;
        public float EffectiveSpeed => m_character != null ? m_character.MoveSpeed * CurrentSpeedMultiplier : 0.0f;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();

            // 必殺技中の「頭が一番下・積み木が上」の車輪演出
            m_wheel = GetComponent<KidaruWheelVisual>();
            if (m_wheel == null) m_wheel = gameObject.AddComponent<KidaruWheelVisual>();

            // インスペクタや実行時追加で0になっていても、必ず時間が延びるようにする
            if (m_baseDuration <= 0.0f) m_baseDuration = 3.0f;
            if (m_durationPerBlock <= 0.0f) m_durationPerBlock = 1.0f;
            if (m_speedMultiplier <= 0.0f) m_speedMultiplier = 1.5f;
        }

        private void Update()
        {
            // 震脚は無敵・虹色・車輪を使わない
            bool active = IsActive && m_character != null && !m_useQuake;
            if (m_wheel != null)
            {
                if (active) m_wheel.Begin();
                else m_wheel.End();
            }
            if (active)
            {
                if (!m_rainbowActive) BeginRainbow();
            }
            else if (m_rainbowActive)
            {
                EndRainbow();
            }

            if (m_gauge == null || !IsLocalHuman()) return;

            if (SpecialInput.WasPressed(m_character)) TryActivate();
        }

        // 既存の無敵処理はUpdateでRendererを点滅させる。
        // この必殺技中は点滅させず、虹色の発光で無敵を示す。
        private void LateUpdate()
        {
            if (m_useQuake) return;
            if (!IsActive || m_character == null || !m_character.IsInvincible) return;

            foreach (var renderer in m_character.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null) renderer.enabled = true;
            }

            // 点滅を戻したあとに色を更新する(色の変化を滑らかに見せる)
            ApplyRainbow();
        }

        // キャラクターの移動処理より先に速度倍率を掛ける
        private void FixedUpdate()
        {
            if (!IsActive || m_character == null || !m_character.CanBeAffected) return;
            if (m_useQuake)
            {
                UpdateQuake();
                return;
            }

            m_character.RequestSpeedScale(Mathf.Max(1.0f, m_speedMultiplier));
        }

        // 震脚: 跳び上がったのを覚えておき、着地した瞬間に衝撃波を出す
        // (DarumaCharacter より先に動くので、IsGrounded は前の物理ステップの結果)
        void UpdateQuake()
        {
            bool grounded = m_character.IsGrounded;
            if (!grounded && m_character.Rigidbody != null && m_character.Rigidbody.linearVelocity.y > 1.0f)
            {
                m_quakeJumping = true;
                return;
            }
            if (!m_quakeJumping || !grounded) return;
            m_quakeJumping = false;
            int hits = KidaruQuake.Emit(m_character, m_quakeRadius);
            if (m_logSpecial) Debug.Log($"[きだる] 震脚の衝撃波 ({hits}人に命中)", this);
        }

        // CPUのきだるが、人間のキー入力で必殺技を出さないようにする
        bool IsLocalHuman()
        {
            if (DarumaMatch.Current == null) return true;   // ラボ

            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) return !p.IsCpu;
            }
            return false;
        }

        // ラボのボタンからも呼べる。発動できたら true
        public bool TryActivate()
        {
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || !m_character.CanBeAffected) return false;
            if (IsActive) return false;   // 強化中は重ねがけしない
            if (m_useQuake) return ActivateQuake();

            int blocks = m_character.Stack != null ? m_character.Stack.Count : 0;
            float duration = m_baseDuration + m_durationPerBlock * blocks;
            LastBlocks = blocks;
            LastDuration = duration;

            m_gauge.Consume();

            // 積み木を吸収するカットインを先に再生する。
            // 実ゲームの積み木は消費せず、見た目だけを複製する。
            var cutInBlocks = new List<TumikiBlock>(Mathf.Max(0, blocks));
            for (int i = 0; i < blocks; i++)
            {
                var stackBlock = m_character.Stack.Get(i);
                if (stackBlock != null) cutInBlocks.Add(stackBlock);
            }
            // ネット対戦用(suehara): クライアントでも同じカットイン・虹色・車輪を出す(無敵・速さはホストが決める)
            SpecialNet.Send(m_character, this, kNetActivate, new[] { duration }, cutInBlocks.ToArray());
            PlayActivate(duration, cutInBlocks);

            if (m_logSpecial)
                Debug.Log($"[きだる] 必殺技『黄金化』発動: 無敵+速度UP {duration:F1}秒 (積み木{blocks}個)", this);
            return true;
        }

        // カットインを再生して、効果時間のあいだ無敵・虹色・車輪にする(ホストとクライアントで同じ)
        void PlayActivate(float duration, IList<TumikiBlock> cutInBlocks)
        {
            KidaruCutIn.Show(m_cutInName, m_character, cutInBlocks);

            m_character.SetInvincible(duration);
            m_activeUntil = Time.time + duration;
            if (m_wheel != null) m_wheel.Begin();
            BeginRainbow();
        }

        //========================================
        // 震脚(suehara)
        //========================================

        bool ActivateQuake()
        {
            m_gauge.Consume();
            // ネット対戦用(suehara): クライアントにも発動を知らせる(衝撃波の当たり判定はホストだけ)
            SpecialNet.Send(m_character, this, kNetQuake, new[] { m_quakeDuration });
            PlayQuake(m_quakeDuration);
            if (m_logSpecial) Debug.Log($"[きだる] 必殺技『{m_quakeName}』発動: {m_quakeDuration:F1}秒", this);
            return true;
        }

        void PlayQuake(float duration)
        {
            m_activeUntil = Time.time + duration;
            LastDuration = duration;
            LastBlocks = m_character.Stack != null ? m_character.Stack.Count : 0;
            // 発動する前から跳んでいた分では出さない(発動のあと跳んで着地したら出る)
            m_quakeJumping = false;
            SpecialCutIn.Show(m_quakeName, m_quakeCutInColor, null, m_character, null, LastBlocks);
        }

        //========================================
        // ネット対戦用(suehara): クライアントで、ホストの発動と同じ見た目を出す
        //========================================

        const int kNetActivate = 0;
        const int kNetQuake = 1;

        public void NetReceive(int ev, float[] v, TumikiBlock[] blocks)
        {
            if (ev == kNetQuake)
            {
                if (!IsActive && v.Length >= 1) PlayQuake(v[0]);
                return;
            }
            if (ev != kNetActivate || v.Length < 1 || IsActive) return;
            var list = new List<TumikiBlock>();
            foreach (var b in blocks) if (b != null) list.Add(b);
            LastBlocks = list.Count;
            LastDuration = v[0];
            PlayActivate(v[0], list);
        }

        void BeginRainbow()
        {
            if (m_rainbowActive || m_character == null) return;

            var all = m_character.GetComponentsInChildren<Renderer>(true);
            var list = new List<Renderer>();
            foreach (var renderer in all)
            {
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    list.Add(renderer);
            }

            m_renderers = list.ToArray();
            m_originalMaterials = new Material[m_renderers.Length][];
            m_rainbowMaterials = new Material[m_renderers.Length][];

            for (int i = 0; i < m_renderers.Length; i++)
            {
                var originals = m_renderers[i].sharedMaterials;
                m_originalMaterials[i] = originals;

                var copies = new Material[originals.Length];
                for (int j = 0; j < originals.Length; j++)
                {
                    if (originals[j] == null) continue;
                    copies[j] = new Material(originals[j]);
                    copies[j].EnableKeyword("_EMISSION");
                }

                m_rainbowMaterials[i] = copies;
                m_renderers[i].sharedMaterials = copies;
            }

            m_rainbowActive = true;
            ApplyRainbow();
        }

        void ApplyRainbow()
        {
            if (!m_rainbowActive || m_rainbowMaterials == null) return;

            float hue = Mathf.Repeat(Time.time * m_rainbowSpeed, 1.0f);
            Color rainbow = Color.HSVToRGB(hue, 0.85f, 1.0f);
            Color emission = rainbow * Mathf.Max(1.0f, m_emissionIntensity);

            for (int i = 0; i < m_rainbowMaterials.Length; i++)
            {
                var materials = m_rainbowMaterials[i];
                if (materials == null) continue;

                for (int j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null) continue;

                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", rainbow);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", rainbow);
                    if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission);
                }
            }
        }

        void EndRainbow()
        {
            if (!m_rainbowActive) return;

            if (m_renderers != null)
            {
                for (int i = 0; i < m_renderers.Length; i++)
                {
                    // レンダラーが先に消えていても、生成したマテリアルは破棄する
                    if (m_rainbowMaterials != null && i < m_rainbowMaterials.Length && m_rainbowMaterials[i] != null)
                    {
                        foreach (var material in m_rainbowMaterials[i])
                        {
                            if (material != null) Destroy(material);
                        }
                    }

                    if (m_renderers[i] == null) continue;
                    if (m_originalMaterials != null && m_originalMaterials[i] != null)
                        m_renderers[i].sharedMaterials = m_originalMaterials[i];
                    m_renderers[i].enabled = true;
                }
            }

            m_renderers = null;
            m_originalMaterials = null;
            m_rainbowMaterials = null;
            m_rainbowActive = false;
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character,
                IsActive ? (m_useQuake ? $"QUAKE {RemainingSeconds:F1}s" : $"POWER UP {RemainingSeconds:F1}s") : null);
        }

        private void OnDisable()
        {
            if (m_wheel != null) m_wheel.End();
            EndRainbow();
        }

        private void OnDestroy()
        {
            if (m_wheel != null) m_wheel.End();
            EndRainbow();
        }
    }
}
