using System.Collections;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube.Lab
{
    // キャラクター確認用のラボ。セレクト画面・CPU・試合ルールを通さず、
    // 積み木の色と数をその場で用意して、パッシブと演出だけを確認する。
    // 舞台・カメラ・ライトは実行時に組み立てるので、シーンにはこのコンポーネントを1つ置くだけでよい。
    [DefaultExecutionOrder(-500)]
    public class CharacterLab : MonoBehaviour
    {
        [Header("参照")]
        [Tooltip("キャラクター一覧。prefab が入っている CharacterData だけを並べる")]
        [SerializeField] SelectRoster m_roster;
        [Tooltip("ダミーに使うキャラクター(パッシブなしの DarumaCharacter)")]
        [SerializeField] GameObject m_dummyPrefab;
        [Tooltip("積み木のプレハブ(Tumiki)")]
        [SerializeField] GameObject m_blockPrefab;

        [Header("舞台")]
        [SerializeField] float m_arenaSize = 30.0f;
        [SerializeField] float m_wallHeight = 2.0f;
        [SerializeField] Vector3 m_playerSpawn = new Vector3(0.0f, 0.1f, -4.0f);
        [SerializeField] Vector3 m_dummySpawn = new Vector3(0.0f, 0.1f, 4.0f);

        [Header("カメラ")]
        [SerializeField] float m_cameraDistance = 14.0f;
        [SerializeField] float m_cameraYaw = 0.0f;
        [SerializeField] float m_cameraPitch = 55.0f;
        [SerializeField] bool m_follow = false;

        static readonly Key[] kCharacterKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
        };

        readonly List<CharacterData> m_characters = new List<CharacterData>();
        int m_index;
        DarumaCharacter m_character;
        CharacterLabDummy m_dummy;
        CharacterLabBlocks m_blocks;
        CharacterLabUI m_ui;
        Camera m_camera;
        GameObject m_arenaRoot;
        float m_yaw, m_pitch, m_distance;
        bool m_slow;
        TumikiColor m_selectedColor = TumikiColor.Red;
        bool m_dummyInvincible = true;
        Coroutine m_dummyRespawnRoutine;
        DarumaCharacter m_dummyCharacter;

        // ---- 外から読むもの(UI用) ----
        public IReadOnlyList<CharacterData> Characters => m_characters;
        public int CurrentIndex => m_index;
        public CharacterData Current => m_characters.Count > 0 ? m_characters[m_index] : null;
        public DarumaCharacter Character => m_character;
        public CharacterLabDummy Dummy => m_dummy;
        public CharacterLabBlocks Blocks => m_blocks;
        public bool SlowMotion => m_slow;
        public bool DummyInvincible => m_dummyInvincible;
        public bool Follow { get => m_follow; set => m_follow = value; }
        public TumikiColor SelectedColor { get => m_selectedColor; set => m_selectedColor = value; }
        public float ShotSpeed => m_character != null && m_character.Hammer != null && m_character.Hammer.Params != null
            ? m_character.Hammer.Params.ShotSpeed : 0.0f;

        private void Awake()
        {
            // 前のシーンでスローのまま終わっていても戻す
            Time.timeScale = 1.0f;
            BuildCharacterList();
            BuildArena();
            BuildHelpers();
            BuildCamera();
            SpawnDummy();
            SelectCharacter(0);
        }

        private void OnDestroy()
        {
            // ラボを閉じたあとにスローが残らないようにする
            Time.timeScale = 1.0f;
        }

        //========================================
        // 組み立て
        //========================================

        void BuildCharacterList()
        {
            m_characters.Clear();
            if (m_roster == null) return;
            foreach (var data in m_roster.characters)
            {
                // まだプレハブが無いキャラ(hebi など)は並べない
                if (data != null && data.prefab != null) m_characters.Add(data);
            }
        }

        void BuildHelpers()
        {
            var blocksGo = new GameObject("CharacterLabBlocks");
            m_blocks = blocksGo.AddComponent<CharacterLabBlocks>();
            m_blocks.Initialize(m_blockPrefab);

            var uiGo = new GameObject("CharacterLabUI");
            var ui = uiGo.AddComponent<CharacterLabUI>();
            ui.Initialize(this);
            m_ui = ui;
        }

        void BuildArena()
        {
            m_arenaRoot = new GameObject("Arena");

            var floorMat = MakeMaterial("LabFloor", new Color(0.72f, 0.75f, 0.78f));
            var wallMat = MakeMaterial("LabWall", new Color(0.42f, 0.47f, 0.52f));
            float half = m_arenaSize * 0.5f;

            CreateCube("Floor", new Vector3(0.0f, -0.5f, 0.0f),
                new Vector3(m_arenaSize, 1.0f, m_arenaSize), floorMat, null);
            CreateCube("Wall_North", new Vector3(0.0f, m_wallHeight * 0.5f, half),
                new Vector3(m_arenaSize + 1.0f, m_wallHeight, 1.0f), wallMat, "Wall");
            CreateCube("Wall_South", new Vector3(0.0f, m_wallHeight * 0.5f, -half),
                new Vector3(m_arenaSize + 1.0f, m_wallHeight, 1.0f), wallMat, "Wall");
            CreateCube("Wall_East", new Vector3(half, m_wallHeight * 0.5f, 0.0f),
                new Vector3(1.0f, m_wallHeight, m_arenaSize + 1.0f), wallMat, "Wall");
            CreateCube("Wall_West", new Vector3(-half, m_wallHeight * 0.5f, 0.0f),
                new Vector3(1.0f, m_wallHeight, m_arenaSize + 1.0f), wallMat, "Wall");

            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(m_arenaRoot.transform);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            RenderSettings.fog = false;
        }

        GameObject CreateCube(string name, Vector3 position, Vector3 scale, Material material, string tag)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(m_arenaRoot.transform);
            go.transform.position = position;
            go.transform.localScale = scale;
            if (!string.IsNullOrEmpty(tag)) go.tag = tag;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return go;
        }

        Material MakeMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader);
            material.name = name;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            return material;
        }

        void BuildCamera()
        {
            var go = new GameObject("Lab Camera");
            go.tag = "MainCamera";
            m_camera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            m_camera.fieldOfView = 55.0f;
            m_camera.nearClipPlane = 0.1f;
            m_camera.farClipPlane = 200.0f;
            m_yaw = m_cameraYaw;
            m_pitch = m_cameraPitch;
            m_distance = m_cameraDistance;
        }

        void SpawnDummy()
        {
            if (m_dummyPrefab == null) return;
            var go = Instantiate(m_dummyPrefab, m_dummySpawn, Quaternion.LookRotation(Vector3.back));
            var character = go.GetComponent<DarumaCharacter>();
            if (character == null)
            {
                Destroy(go);
                return;
            }
            m_dummyCharacter = character;
            character.Eliminated += OnDummyEliminated;
            m_dummy = go.AddComponent<CharacterLabDummy>();
            m_dummy.Initialize(character, m_dummyInvincible);
        }

        // ダミーが倒れたら、少し待って同じ場所に出す
        void OnDummyEliminated(DarumaCharacter character)
        {
            if (!ReferenceEquals(character, m_dummyCharacter)) return;
            m_dummyCharacter = null;
            m_dummy = null;
            if (m_dummyRespawnRoutine == null)
                m_dummyRespawnRoutine = StartCoroutine(RespawnDummyLater());
        }

        IEnumerator RespawnDummyLater()
        {
            // スローモーション中でも待ちすぎないよう、現実の時間で待つ
            yield return new WaitForSecondsRealtime(0.8f);
            m_dummyRespawnRoutine = null;
            SpawnDummy();
        }

        public void RespawnDummyNow()
        {
            if (m_dummyRespawnRoutine != null)
            {
                StopCoroutine(m_dummyRespawnRoutine);
                m_dummyRespawnRoutine = null;
            }
            if (m_dummyCharacter != null)
            {
                m_dummyCharacter.gameObject.SetActive(false);
                Destroy(m_dummyCharacter.gameObject);
            }
            m_dummyCharacter = null;
            m_dummy = null;
            SpawnDummy();
        }

        public void SetDummyInvincible(bool invincible)
        {
            m_dummyInvincible = invincible;
            if (m_dummy != null) m_dummy.SetInvincible(invincible);
        }

        //========================================
        // キャラクター
        //========================================

        public void SelectCharacter(int index)
        {
            if (m_characters.Count == 0) return;
            index = Mathf.Clamp(index, 0, m_characters.Count - 1);
            if (index == m_index && m_character != null) return;
            m_index = index;
            RespawnCharacter();
        }

        public void SelectCharacter(string id)
        {
            int index = m_characters.FindIndex(c => c.id == id);
            if (index >= 0) SelectCharacter(index);
        }

        void RespawnCharacter()
        {
            if (m_character != null)
            {
                if (m_blocks != null) m_blocks.ClearStack(m_character);
                // 同じ位置に出すので、消えるまでの1フレームだけ古い体を止める
                m_character.gameObject.SetActive(false);
                Destroy(m_character.gameObject);
                m_character = null;
            }

            var data = Current;
            if (data == null || data.prefab == null) return;
            var go = Instantiate(data.prefab, m_playerSpawn, Quaternion.LookRotation(Vector3.forward));
            var character = go.GetComponent<DarumaCharacter>();
            if (character == null)
            {
                Destroy(go);
                return;
            }
            // キーボードで遊ぶ(DarumaCharacter は Awake で入力元を探すので、あとから差し替える)
            var input = go.AddComponent<KeyboardDarumaInput>();
            character.SetCommandSource(input);
            m_character = character;
        }

        public void AddBlocks(DarumaCharacter target, TumikiColor color, int count)
        {
            if (m_blocks == null) return;
            m_blocks.AddToStack(target, color, count);
        }

        public void ClearStack(DarumaCharacter target)
        {
            if (m_blocks == null) return;
            m_blocks.ClearStack(target);
        }

        public void RemoveBlocks(DarumaCharacter target, TumikiColor color)
        {
            if (m_blocks == null) return;
            m_blocks.RemoveFromStack(target, color);
        }

        public void Launch(TumikiColor color, float speed, float angle)
        {
            if (m_blocks == null || m_character == null || m_dummy == null) return;
            Vector3 dir = m_dummy.transform.position - m_character.transform.position;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.001f) dir = m_character.transform.forward;
            dir.Normalize();
            if (Mathf.Abs(angle) > 0.01f) dir = Quaternion.Euler(0.0f, angle, 0.0f) * dir;
            m_blocks.Launch(m_character, color, dir, speed);
        }

        //========================================
        // 操作
        //========================================

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < m_characters.Count && i < kCharacterKeys.Length; i++)
            {
                if (keyboard[kCharacterKeys[i]].wasPressedThisFrame) SelectCharacter(i);
            }

            bool toDummy = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (keyboard.rKey.wasPressedThisFrame) AddColor(TumikiColor.Red, toDummy);
            if (keyboard.gKey.wasPressedThisFrame) AddColor(TumikiColor.Green, toDummy);
            if (keyboard.bKey.wasPressedThisFrame) AddColor(TumikiColor.Blue, toDummy);
            if (keyboard.yKey.wasPressedThisFrame) AddColor(TumikiColor.Yellow, toDummy);
            if (keyboard.vKey.wasPressedThisFrame) Launch(m_selectedColor, 20.0f, 0.0f);
            if (keyboard.tKey.wasPressedThisFrame) ToggleSlow();
            if (keyboard.fKey.wasPressedThisFrame) m_follow = !m_follow;
            if (keyboard.pKey.wasPressedThisFrame) ResetPositions();
            if (keyboard.iKey.wasPressedThisFrame) SetDummyInvincible(!m_dummyInvincible);
        }

        void AddColor(TumikiColor color, bool toDummy)
        {
            m_selectedColor = color;
            var target = toDummy && m_dummy != null ? m_dummy.Character : m_character;
            AddBlocks(target, color, 1);
        }

        private void LateUpdate()
        {
            HandleCameraInput();
            UpdateCamera();
        }

        void HandleCameraInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.isPressed) m_yaw -= 60.0f * Time.unscaledDeltaTime;
                if (keyboard.eKey.isPressed) m_yaw += 60.0f * Time.unscaledDeltaTime;
                if (keyboard.zKey.isPressed) m_distance += 10.0f * Time.unscaledDeltaTime;
                if (keyboard.xKey.isPressed) m_distance -= 10.0f * Time.unscaledDeltaTime;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;
            // UIの上ではホイール・右ドラッグをカメラに使わない(パネルのスクロールを優先)
            bool overUI = m_ui != null && m_ui.IsPointerOverUI(mouse.position.ReadValue());
            if (!overUI && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                m_yaw += delta.x * 0.3f;
                m_pitch -= delta.y * 0.2f;
            }
            if (overUI) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.1f)
            {
                // 1ノッチ(120)で約3.5m動かす。以前の約10倍の速さ
                m_distance -= (scroll / 120.0f) * 3.5f;
            }
        }

        void UpdateCamera()
        {
            if (m_camera == null) return;
            m_pitch = Mathf.Clamp(m_pitch, 20.0f, 80.0f);
            m_distance = Mathf.Clamp(m_distance, 4.0f, 30.0f);

            Vector3 target = m_follow && m_character != null
                ? m_character.transform.position + Vector3.up * 1.0f
                : new Vector3(0.0f, 1.2f, 0.0f);
            Quaternion rotation = Quaternion.Euler(m_pitch, m_yaw, 0.0f);
            Vector3 position = target - rotation * Vector3.forward * m_distance;
            m_camera.transform.SetPositionAndRotation(position, rotation);
        }

        public void ToggleSlow()
        {
            m_slow = !m_slow;
            Time.timeScale = m_slow ? 0.2f : 1.0f;
        }

        public void ResetPositions()
        {
            if (m_character != null)
            {
                var rb = m_character.Rigidbody;
                if (rb != null)
                {
                    rb.position = m_playerSpawn;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                m_character.transform.SetPositionAndRotation(m_playerSpawn, Quaternion.LookRotation(Vector3.forward));
            }
            if (m_dummy != null) m_dummy.ResetPosition();
        }

        public void ResetAll()
        {
            if (m_blocks != null)
            {
                m_blocks.ClearStack(m_character);
                if (m_dummy != null) m_blocks.ClearStack(m_dummy.Character);
                m_blocks.DestroyLoose();
            }
            ResetPositions();
        }
    }
}
