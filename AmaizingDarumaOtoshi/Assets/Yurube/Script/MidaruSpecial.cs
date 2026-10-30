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
    public class MidaruSpecial : MonoBehaviour, ISpecialAbility
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

        DarumaCharacter m_character;
        SpecialGauge m_gauge;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
        }

        private void Update()
        {
            if (m_gauge == null || !IsLocalHuman()) return;

            var keyboard = Keyboard.current;
            bool pressed = keyboard != null && keyboard.lKey.wasPressedThisFrame;

            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame) pressed = true;

            if (pressed) TryActivate();
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
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || m_character.Stack == null || m_character.Stack.Count == 0) return false;
            if (!m_character.CanBeAffected) return false;
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            // 撃つ前の積み木の数 = 分裂する数
            int count = m_character.Stack.Count;

            var block = m_character.Stack.RemoveAt(0);
            if (block == null) return false;
            m_gauge.Consume();

            Vector3 direction = m_character.transform.forward;
            Vector3 center = m_character.transform.position + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            Vector3 position = center + direction * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
            bool embedded = Nakagawa.TumikiPlacement.ResolveEmbed(center, ref position);
            block.transform.position = position;

            Split(block, Mathf.Max(1, count), position, direction);
            if (embedded) Nakagawa.TumikiPlacement.IgnoreCharactersAfterEmbed(block);

            if (m_logSpecial) Debug.Log($"[みだる] 必殺技：{Mathf.Max(1, count)}発のファンネルに分裂", this);
            return true;
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
                enemies.Add(character);
            }

            enemies.Sort((a, b) =>
                (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            return enemies;
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            if (m_gauge == null || DarumaMatch.Current == null) return;

            MatchPlayer me = null;
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) { me = p; break; }
            }
            if (me == null || me.IsCpu) return;

            var rect = new Rect(20.0f, Screen.height - 64.0f, 260.0f, 34.0f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8.0f, rect.y + 2.0f, 244.0f, 18.0f),
                m_gauge.IsFull ? "SPECIAL READY [L / Y]" : $"SPECIAL {m_gauge.Blocks}/{m_gauge.BlocksToFill}");

            var bar = new Rect(rect.x + 8.0f, rect.y + 22.0f, 244.0f, 6.0f);
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = m_gauge.IsFull ? Color.cyan : new Color(0.2f, 0.7f, 1.0f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * m_gauge.Normalized, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
