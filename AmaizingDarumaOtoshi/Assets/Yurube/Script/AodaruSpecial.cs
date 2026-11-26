using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // あおだるの必殺技「六腕」。
    // 発動すると6本のハンマー(阿修羅のような腕)を出し、
    // プレイヤーを起点にした3方向から近づく積み木を、操作に関係なくはじき返す。
    [RequireComponent(typeof(DarumaCharacter))]
    public class AodaruSpecial : MonoBehaviour, ISpecialAbility, INetSpecial
    {
        [Header("必殺技")]
        [Tooltip("効果時間(秒)")]
        [SerializeField] float m_duration = 4.0f;
        [Tooltip("積み木をはじき返す範囲(m)")]
        [SerializeField] float m_knockRadius = 3.0f;
        [Tooltip("はじき返す速さ(m/s)")]
        [SerializeField] float m_knockSpeed = 24.0f;
        [Tooltip("同じ積み木を続けてはじかない間隔(秒)")]
        [SerializeField] float m_knockInterval = 0.5f;
        [Tooltip("発動をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        [Header("カットイン")]
        [Tooltip("画面下部に表示する必殺技名")]
        [SerializeField] string m_cutInName = "六腕";

        [Header("疾風連撃(suehara)。オンなら上の「六腕」の代わりにこちらを出す")]
        [Tooltip("オン: 薙ぎ払うたびに前へ斬撃が飛ぶ / オフ: 前の「六腕」")]
        [SerializeField] bool m_useGaleCombo = true;
        [Tooltip("技名(カットインに出す)")]
        [SerializeField] string m_galeName = "疾風連撃";
        [Tooltip("効果時間(秒)")]
        [SerializeField] float m_galeDuration = 5.0f;
        [Tooltip("斬撃の進む速さ(m/s)。遅いほどジャンプでよけやすい")]
        [SerializeField] float m_slashSpeed = 11.0f;
        [Tooltip("斬撃が届く距離(m)")]
        [SerializeField] float m_slashRange = 12.0f;
        [Tooltip("斬撃の横幅(m)")]
        [SerializeField] float m_slashWidth = 2.4f;
        [Tooltip("斬撃の高さ(足元から。m)。ジャンプで足元がこれより上にあれば当たらない")]
        [SerializeField] float m_slashHeight = 0.6f;
        [Tooltip("斬撃の厚み(進む向き。m)。厚いほどジャンプでよけにくい")]
        [SerializeField] float m_slashDepth = 0.4f;
        [SerializeField] Color m_galeCutInColor = new Color(0.35f, 0.75f, 1.0f);

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        AodaruSixArms m_arms;
        // 疾風連撃が終わる時刻(使っていなければ負)・この振りで斬撃を出したか
        float m_galeUntil = -1.0f;
        // 最後に斬撃を出した振りの番号(DarumaHammer.SweepCount。切り返しで続けて振っても1振りに1本)
        int m_slashedSweep = -1;

        public bool IsActive => m_arms != null || m_galeUntil >= 0.0f;
        // CPU 用(suehara): 疾風連撃を使うか / 斬撃が届く距離
        public bool UsesGaleCombo => m_useGaleCombo;
        public float SlashRange => m_slashRange;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
        }

        private void Update()
        {
            UpdateGale();
            if (m_gauge == null || !IsLocalHuman()) return;

            if (SpecialInput.WasPressed(m_character)) TryActivate();
        }

        // CPUのあおだるが、人間のキー入力で必殺技を出さないようにする
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
            if (m_useGaleCombo) return TryActivateGale();
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || !m_character.CanBeAffected) return false;
            if (m_arms != null) return false;   // 発動中は重ねられない
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            m_gauge.Consume();

            float yaw = m_character.transform.eulerAngles.y;
            // ネット対戦用(suehara): クライアントでも同じ見た目を出す(積み木をはじくのはホストだけ。AodaruSixArms)
            SpecialNet.Send(m_character, this, kNetActivate, new[] { yaw });
            PlayActivate(yaw);

            if (m_logSpecial) Debug.Log("[あおだる] 必殺技『六腕』発動", this);
            return true;
        }

        // 六腕を出してカットインを再生する(ホストとクライアントで同じ)
        void PlayActivate(float yaw)
        {
            var root = new GameObject("AodaruSixArms");
            m_arms = root.AddComponent<AodaruSixArms>();

            var passive = GetComponent<AodaruPassive>();
            Material energySource = passive != null ? passive.EffectMaterial : null;

            m_arms.Initialize(
                m_character,
                m_duration,
                yaw,
                m_knockRadius,
                m_knockSpeed,
                m_knockInterval,
                energySource);

            // あかだる・いわだる・へびだると同じタイミングで専用カットインを再生する
            AodaruCutIn.Show(m_cutInName, m_character);
        }

        //========================================
        // 疾風連撃(suehara)
        //  効果時間の間、薙ぎ払いのたびに(振りの半分まで来たところで)前へ斬撃が飛ぶ
        //========================================

        bool TryActivateGale()
        {
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || !m_character.CanBeAffected) return false;
            if (m_galeUntil >= 0.0f) return false;   // 発動中は重ねられない

            m_gauge.Consume();
            // ネット対戦用(suehara): クライアントにも発動を知らせる(斬撃の当たり判定はホストだけ)
            SpecialNet.Send(m_character, this, kNetGale, new[] { 0.0f });
            PlayGale();

            if (m_logSpecial) Debug.Log($"[あおだる] 必殺技『{m_galeName}』発動", this);
            return true;
        }

        void PlayGale()
        {
            m_galeUntil = Time.time + m_galeDuration;
            // 発動する前から振っていた振りでは出さない
            m_slashedSweep = m_character.Hammer != null ? m_character.Hammer.SweepCount : -1;
            SpecialCutIn.Show(m_galeName, m_galeCutInColor, null, m_character, null, m_character.Stack != null ? m_character.Stack.Count : 0);
        }

        void UpdateGale()
        {
            if (m_galeUntil < 0.0f || m_character == null) return;
            DarumaHammer hammer = m_character.Hammer;
            if (hammer == null) return;

            // 時間はゲームの時間で数える(カットインで止まっている間は減らない)
            if (Time.time >= m_galeUntil)
            {
                m_galeUntil = -1.0f;
                return;
            }

            if (!hammer.IsSweeping || hammer.SweepCount == m_slashedSweep) return;
            if (hammer.ActionTime < hammer.SweepDuration * 0.5f) return;
            m_slashedSweep = hammer.SweepCount;
            if (SpecialNet.IsClient) return;   // ネット対戦用(suehara): 斬撃はホストだけ

            Vector3 dir = m_character.transform.forward;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            Vector3 from = m_character.transform.position + dir * (DarumaCharacter.kBodyRadius + 0.3f);
            AodaruGaleSlash.Spawn(m_character, from, dir, m_slashSpeed, m_slashRange, m_slashWidth, m_slashHeight, m_slashDepth);
        }

        //========================================
        // ネット対戦用(suehara): クライアントで、ホストの発動と同じ見た目を出す
        //========================================

        const int kNetActivate = 0;
        const int kNetGale = 1;

        public void NetReceive(int ev, float[] v, TumikiBlock[] blocks)
        {
            if (ev == kNetGale)
            {
                if (m_galeUntil < 0.0f) PlayGale();
                return;
            }
            if (ev != kNetActivate || v.Length < 1 || m_arms != null) return;
            PlayActivate(v[0]);
        }

        private void OnDestroy()
        {
            if (m_arms != null) Destroy(m_arms.gameObject);
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character);
        }
    }
}
