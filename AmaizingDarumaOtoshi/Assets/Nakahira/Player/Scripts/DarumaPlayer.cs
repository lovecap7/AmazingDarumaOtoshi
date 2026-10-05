using UnityEngine;

namespace Nakahira
{
    // プレイヤー(操作者)。操作命令の供給元を持ち、今操作しているキャラクターに渡す
    //  生存キャラクター ─(脱落)→ 幽霊 ─(生存者にダメージ)→ 生存キャラクター(首だけで復活)
    // 同じGameObjectの IDarumaCommandSource(DarumaInputHandler等)を使う。無ければ操作なし(ダミー)
    public class DarumaPlayer : MonoBehaviour
    {
        IDarumaCommandSource m_commandSource;
        DarumaMatch m_match;

        public int Index { get; private set; }
        public DarumaCharacter Character { get; private set; }
        public DarumaGhost Ghost { get; private set; }
        public bool IsSurvivor => Character != null;
        // 現在操作しているキャラクターのTransform(いなければnull)
        public Transform CurrentBody => Character != null ? Character.transform : Ghost != null ? Ghost.transform : null;

        private void Awake()
        {
            m_commandSource = GetComponent<IDarumaCommandSource>();
        }

        private void Start()
        {
            // PlayerInputManagerから参加した場合はここで試合に登録する
            if (m_match != null) return;
            var match = FindFirstObjectByType<DarumaMatch>();
            if (match == null || !match.Register(this))
            {
                Destroy(gameObject);
            }
        }

        // DarumaMatchから呼ばれる
        public void OnRegistered(DarumaMatch match, int index)
        {
            m_match = match;
            Index = index;
            name = $"Player{index + 1}";
        }

        public void Possess(DarumaCharacter character)
        {
            Character = character;
            character.name = $"{name}_Character";
            character.SetCommandSource(m_commandSource);
            character.Eliminated += OnCharacterEliminated;
        }

        // 試合終了などで操作を止める
        public void StopControl()
        {
            if (Character != null) Character.SetCommandSource(null);
            if (Ghost != null) Ghost.SetCommandSource(null);
        }

        private void OnCharacterEliminated(DarumaCharacter character)
        {
            Character = null;
            m_match.NotifyEliminated(this);
            if (m_match.IsFinished) return;

            // 脱落した場所に幽霊として出現
            Ghost = m_match.SpawnGhost(character.transform.position);
            Ghost.name = $"{name}_Ghost";
            Ghost.SetCommandSource(m_commandSource);
            Ghost.ReviveRequested += OnGhostReviveRequested;
        }

        private void OnGhostReviveRequested(DarumaGhost ghost)
        {
            if (m_match.IsFinished) return;

            Ghost = null;
            Destroy(ghost.gameObject);
            Possess(m_match.SpawnCharacter(this, true));
        }
    }
}
 