using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakahira
{
    // 試合の管理。プレイヤーの登録、キャラクター/幽霊の生成、試合終了判定を行う
    // 同じGameObjectに PlayerInputManager があれば、ゲームパッドのボタンを押したプレイヤーが参加する
    public class DarumaMatch : MonoBehaviour
    {
        [Header("ステージ")]
        // 出現・復活ポイント。他のキャラクターすべてから最も遠い地点が選ばれる
        [SerializeField] Transform[] m_spawnPoints;
        [SerializeField] StageArea m_stageArea;

        [Header("キャラクター")]
        [SerializeField] DarumaCharacter m_characterPrefab;
        [SerializeField] DarumaGhost m_ghostPrefab;
        [SerializeField] float m_reviveInvincibleTime = 1.5f;

        [Header("参加")]
        [SerializeField] int m_maxPlayers = 4;

        [Header("デバッグ")]
        // 開始時に置く操作なしのダミープレイヤーの数
        [SerializeField] int m_dummyCount = 1;
        // ダミーに最初から積んでおく積み木
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [SerializeField] int m_dummyStartBlocks = 5;

        readonly List<DarumaPlayer> m_players = new List<DarumaPlayer>();
        PlayerInputManager m_inputManager;

        public bool IsFinished { get; private set; }
        public IReadOnlyList<DarumaPlayer> Players => m_players;

        private void Awake()
        {
            // 定員オーバーで参加したプレイヤーは Register で弾かれて破棄される
            m_inputManager = GetComponent<PlayerInputManager>();
        }

        private void Start()
        {
            for (int i = 0; i < m_dummyCount; i++)
            {
                var dummy = new GameObject("Dummy").AddComponent<DarumaPlayer>();
                if (!Register(dummy))
                {
                    Destroy(dummy.gameObject);
                    break;
                }
                dummy.name += "_Dummy";
                StackDebugBlocks(dummy.Character);
            }
        }

        // 試合に参加させ、キャラクターを出現させる。参加できなければfalse
        public bool Register(DarumaPlayer player)
        {
            if (IsFinished || m_players.Count >= m_maxPlayers) return false;

            m_players.Add(player);
            player.OnRegistered(this, m_players.Count - 1);
            player.Possess(SpawnCharacter(player, false));
            Debug.Log($"[DarumaMatch] {player.name} が参加 ({m_players.Count}/{m_maxPlayers})");
            return true;
        }

        public DarumaCharacter SpawnCharacter(DarumaPlayer player, bool withInvincible)
        {
            Vector3 pos = FindFarthestSpawnPoint(player);
            Vector3 toCenter = m_stageArea.transform.position - pos;
            toCenter.y = 0.0f;
            Quaternion rot = toCenter.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;

            DarumaCharacter character = Instantiate(m_characterPrefab, pos, rot);
            if (withInvincible) character.SetInvincible(m_reviveInvincibleTime);
            return character;
        }

        public DarumaGhost SpawnGhost(Vector3 position)
        {
            // 奈落に落ちた場合もステージ範囲内の床の高さに出す
            Vector3 pos = m_stageArea.Clamp(position, 0.5f);
            DarumaGhost ghost = Instantiate(m_ghostPrefab, pos, Quaternion.identity);
            ghost.Init(m_stageArea);
            return ghost;
        }

        public void NotifyEliminated(DarumaPlayer player)
        {
            Debug.Log($"[DarumaMatch] {player.name} が脱落");
            CheckFinish();
        }

        // 生存者が1人以下になったら試合終了(2人以上参加している場合のみ)
        private void CheckFinish()
        {
            if (IsFinished || m_players.Count < 2) return;

            DarumaPlayer winner = null;
            int survivors = 0;
            foreach (var p in m_players)
            {
                if (!p.IsSurvivor) continue;
                survivors++;
                winner = p;
            }
            if (survivors > 1) return;

            IsFinished = true;
            if (m_inputManager != null) m_inputManager.DisableJoining();
            foreach (var p in m_players) p.StopControl();

            Debug.Log(survivors == 1
                ? $"[DarumaMatch] 試合終了! 勝者: {winner.name}"
                : "[DarumaMatch] 試合終了! 引き分け");
        }

        // 他のキャラクター(生存者・幽霊)すべてから最も遠い出現ポイント
        private Vector3 FindFarthestSpawnPoint(DarumaPlayer self)
        {
            Vector3 best = m_spawnPoints[0].position;
            float bestScore = float.MinValue;
            foreach (var point in m_spawnPoints)
            {
                // 一番近いキャラクターまでの距離が大きいほど安全
                float nearest = float.MaxValue;
                foreach (var p in m_players)
                {
                    if (p == self || p.CurrentBody == null) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(point.position, p.CurrentBody.position));
                }
                if (nearest > bestScore)
                {
                    bestScore = nearest;
                    best = point.position;
                }
            }
            return best;
        }

        private void StackDebugBlocks(DarumaCharacter character)
        {
            if (m_tumikiPrefab == null || character == null) return;
            for (int i = 0; i < m_dummyStartBlocks; i++)
            {
                TumikiBlock block = Instantiate(m_tumikiPrefab, character.transform.position, character.transform.rotation);
                block.SetColor((TumikiColor)(i % 4));
                character.Stack.AddBottom(block);
            }
        }
    }
}
