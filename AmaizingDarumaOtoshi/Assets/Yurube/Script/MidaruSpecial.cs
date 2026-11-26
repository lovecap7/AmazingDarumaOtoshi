using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // みだるの必殺技「ファンネル」。
    // あかだるのトロンと同じく、積み木が1個以上あるときにボタンを押すと
    // その場で自分の積み木を撃ち、撃った一発が
    // 「積んでいる積み木の数(撃つ前の数)」に分裂して近くの敵へホーミングする。
    [RequireComponent(typeof(DarumaCharacter))]
    public class MidaruSpecial : MonoBehaviour, ISpecialAbility, INetSpecial
    {
        [Header("必殺技")]
        [Tooltip("撃ち出す速さ(m/s)")]
        [SerializeField] float m_speed = 20.0f;
        [Tooltip("分裂時の広がり角度(度)")]
        [SerializeField] float m_spreadAngle = 18.0f;
        [Tooltip("分裂弾どうしの位置のずらし(m)")]
        [SerializeField] float m_spawnSpread = 0.45f;
        [Tooltip("分裂弾の旋回速度(度/秒)")]
        [SerializeField] float m_homingTurnSpeed = 360.0f;
        [Tooltip("分裂弾が追いかける時間(秒)")]
        [SerializeField] float m_homingDuration = 3.0f;
        [Tooltip("発射してから曲がり始めるまでの時間(秒)")]
        [SerializeField] float m_homingDelay = 0.15f;
        [Tooltip("分裂弾どうしの当たり判定を無視する")]
        [SerializeField] bool m_ignoreMissileCollisions = true;
        [Tooltip("発動・分裂をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        [Header("カットイン")]
        [Tooltip("画面下部に表示する必殺技名")]
        [SerializeField] string m_cutInName = "ファンネル";

        [Header("マシンガン(suehara)。オンなら上の「ファンネル」の代わりにこちらを出す")]
        [Tooltip("オン: 目の前に固定砲台を置き、砲台が一番近い敵へ自動で向いて弾を連射する(みだるは自由に動ける) / オフ: 前の「ファンネル」")]
        [SerializeField] bool m_useMachineGun = true;
        [Tooltip("技名(カットインに出す)")]
        [SerializeField] string m_machineGunName = "マシンガン";
        [Tooltip("撃ち続ける時間(秒)")]
        [SerializeField] float m_machineGunDuration = 6.0f;
        [Tooltip("1秒に撃つ数")]
        [SerializeField] float m_machineGunRate = 8.0f;
        [Tooltip("弾の速さ(m/s)")]
        [SerializeField] float m_machineGunSpeed = 24.0f;
        [Tooltip("弾が届く距離(m)。0 なら無限(壁などに当たるまで飛ぶ)")]
        [SerializeField] float m_machineGunRange = 0.0f;
        [Tooltip("砲台が回る速さの上限(度/秒)。ダッシュ(9.6m/s)で横に走る相手は、距離5mで毎秒約110°、4mで約140°回り込む。140なら4mより近くでダッシュされると追いつけない")]
        [SerializeField] float m_machineGunTurnSpeed = 140.0f;
        [SerializeField] Color m_machineGunCutInColor = new Color(0.3f, 0.85f, 0.4f);

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        MidaruMachineGun m_gun;

        // 必殺技ゲージ用(中平): 発動してから分裂弾が追いかけ終わるまで(この間はゲージが溜まらない)
        float m_activeUntil = -1.0f;
        public bool IsActive => Time.time < m_activeUntil || (m_gun != null && m_gun.IsFiring);
        // CPU 用(suehara): マシンガンを使うか / 撃った弾が届く目安の距離
        public bool UsesMachineGun => m_useMachineGun;
        public float MachineGunRange => 10.0f;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
        }

        private void Update()
        {
            if (m_gauge == null || !IsLocalHuman()) return;

            if (SpecialInput.WasPressed(m_character)) TryActivate();
        }

        // CPUのみだるが、人間のキー入力で必殺技を出さないようにする
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
            if (m_useMachineGun) return TryActivateMachineGun();
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || m_character.Stack == null || m_character.Stack.Count == 0) return false;
            if (!m_character.CanBeAffected) return false;
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            // 撃つ前の積み木の数 = 分裂する数
            int count = m_character.Stack.Count;
            var cutInBlocks = new List<TumikiBlock>(count);
            for (int i = 0; i < count; i++)
            {
                var stackBlock = m_character.Stack.Get(i);
                if (stackBlock != null) cutInBlocks.Add(stackBlock);
            }

            var block = m_character.Stack.RemoveAt(0);
            if (block == null) return false;
            m_gauge.Consume();
            m_activeUntil = Time.time + m_homingDelay + m_homingDuration;   // 必殺技ゲージ用(中平)

            Vector3 direction = m_character.transform.forward;
            Vector3 center = m_character.transform.position + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            Vector3 position = center + direction * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
            bool embedded = Nakagawa.TumikiPlacement.ResolveEmbed(center, ref position);
            block.transform.position = position;

            // カットインを先に開始し、その間に実際の分裂処理を組み立てる。
            // timeScale=0中は弾が動かず、カットイン終了後に本来の攻撃が始まる。
            // ネット対戦用(suehara): クライアントでも同じカットインを出す(分裂した弾はネットワークに乗った積み木なので、そのまま届く)
            SpecialNet.Send(m_character, this, kNetActivate, null, cutInBlocks.ToArray());
            MidaruCutIn.Show(m_cutInName, m_character, cutInBlocks);

            Split(block, Mathf.Max(1, count), position, direction);
            if (embedded) Nakagawa.TumikiPlacement.IgnoreCharactersAfterEmbed(block);

            if (m_logSpecial) Debug.Log($"[みだる] 必殺技：{Mathf.Max(1, count)}発のファンネルに分裂", this);
            return true;
        }

        //========================================
        // マシンガン(suehara)
        //========================================

        bool TryActivateMachineGun()
        {
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || !m_character.CanBeAffected) return false;
            if (IsActive) return false;   // 砲台が撃っている間は重ねられない

            m_gauge.Consume();
            // 砲台はみだるの目の前に置く(置いたあと、みだるは自由に動ける)
            Vector3 direction = m_character.transform.forward;
            direction.y = 0.0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 position = m_character.transform.position + direction * MidaruMachineGun.HoldDistance;
            // ネット対戦用(suehara): クライアントでも砲台の見た目を出す(撃つのはホストだけ)
            SpecialNet.Send(m_character, this, kNetMachineGun, new[] { position.x, position.y, position.z, direction.x, direction.z });
            PlayMachineGun(position, direction);

            if (m_logSpecial) Debug.Log($"[みだる] 必殺技『{m_machineGunName}』発動", this);
            return true;
        }

        void PlayMachineGun(Vector3 position, Vector3 direction)
        {
            m_gun = MidaruMachineGun.Place(m_character, position, direction, m_machineGunDuration, m_machineGunRate, m_machineGunSpeed, m_machineGunRange, m_machineGunTurnSpeed);
            SpecialCutIn.Show(m_machineGunName, m_machineGunCutInColor, null, m_character, null, m_character.Stack != null ? m_character.Stack.Count : 0);
        }

        //========================================
        // ネット対戦用(suehara): クライアントで、ホストの発動と同じカットインを出す
        //========================================

        const int kNetActivate = 0;
        const int kNetMachineGun = 1;

        public void NetReceive(int ev, float[] v, TumikiBlock[] blocks)
        {
            if (ev == kNetMachineGun)
            {
                if (v.Length >= 5 && m_gun == null) PlayMachineGun(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], 0.0f, v[4]));
                return;
            }
            if (ev != kNetActivate) return;
            var list = new List<TumikiBlock>();
            foreach (var b in blocks) if (b != null) list.Add(b);
            MidaruCutIn.Show(m_cutInName, m_character, list);
        }

        // 撃った1個を count 個に分裂させ、敵へホーミングさせる
        void Split(TumikiBlock block, int count, Vector3 origin, Vector3 baseDirection)
        {
            var missiles = new List<TumikiBlock>(count);
            var colliders = new List<Collider>(count * 2);
            var enemies = CollectEnemies(origin);

            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) * 0.5f) * m_spreadAngle;
                Vector3 direction = Quaternion.Euler(0.0f, offset, 0.0f) * baseDirection;
                Vector3 spawn = origin + direction * (m_spawnSpread * i);

                TumikiBlock missile;
                if (i == 0)
                {
                    missile = block;
                    missile.transform.position = spawn;
                    missile.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
                else
                {
                    missile = SpawnCopy(block, spawn, direction);
                }
                if (missile == null) continue;

                missile.Launch(direction, m_speed, m_character);

                var homing = missile.GetComponent<TumikiHoming>();
                if (homing == null) homing = missile.gameObject.AddComponent<TumikiHoming>();
                homing.Begin(m_character, m_homingTurnSpeed, m_homingDuration, false, m_homingDelay);
                if (enemies.Count > 0) homing.SetPreferredTarget(enemies[i % enemies.Count]);

                missiles.Add(missile);
                colliders.AddRange(missile.GetComponentsInChildren<Collider>());
            }

            // 分裂直後に弾どうしがぶつかって消えないようにする
            if (m_ignoreMissileCollisions)
            {
                for (int a = 0; a < colliders.Count; a++)
                {
                    for (int b = a + 1; b < colliders.Count; b++)
                    {
                        if (colliders[a] != null && colliders[b] != null)
                        {
                            Physics.IgnoreCollision(colliders[a], colliders[b], true);
                        }
                    }
                }
            }
        }

        TumikiBlock SpawnCopy(TumikiBlock source, Vector3 position, Vector3 direction)
        {
            var clone = Instantiate(source.gameObject, position, Quaternion.LookRotation(direction, Vector3.up));
            clone.name = source.name;
            var block = clone.GetComponent<TumikiBlock>();
            if (block != null) block.SetColor(source.Color);
            return block;
        }

        // 近い順に敵を集める(分裂弾を別々の敵に振り分けるため)
        List<DarumaCharacter> CollectEnemies(Vector3 from)
        {
            var enemies = new List<DarumaCharacter>();

            foreach (var character in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (character == null || character == m_character) continue;
                if (DarumaMatch.AreFriends(m_character, character)) continue;   // チーム戦:味方は追わない(中平)
                enemies.Add(character);
            }

            enemies.Sort((a, b) =>
                (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            return enemies;
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character);
        }
    }
}
