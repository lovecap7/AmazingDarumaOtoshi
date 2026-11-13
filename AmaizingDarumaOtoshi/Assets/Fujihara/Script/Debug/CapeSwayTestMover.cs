using UnityEngine;
using UnityEngine.InputSystem;

// マントの揺れの確認用（TestScene だけで使う）。だるまを動かして、マントがなびくか見る。
// 自動（12 秒で1周）：右へ走る → 止まる → 左へ走る → 止まる → その場で1回転 → ジャンプ → 必殺技の溜め（3 秒）→ 放つ
// 手動：WASD / 左スティックで走る（向きも変わる）、Space / A でジャンプ、E / 右トリガー（押している間）で溜め。
//       手動で動かしている間は、自動の動きは止まる
public class CapeSwayTestMover : MonoBehaviour
{
    [SerializeField] bool autoMove = true;
    [SerializeField] float runSpeed = 6f;
    [SerializeField] float runDistance = 5f;
    [SerializeField] float turnSpeed = 360f;
    [SerializeField] float jumpPower = 6f;
    [Tooltip("溜めが最大になるまでの秒数")]
    [SerializeField] float chargeTime = 1.2f;
    [Tooltip("カメラがだるまを追いかける（最初の位置関係のまま、横にだけついていく）")]
    [SerializeField] Camera followCamera;

    const float Cycle = 12f;
    Vector3 home, camOffset;
    float velY, manualTimer, charge;
    bool autoJumped;
    CapeSway cape;

    void Start()
    {
        home = transform.position;
        cape = GetComponentInChildren<CapeSway>();
        if (followCamera == null) followCamera = Camera.main;
        if (followCamera != null) camOffset = followCamera.transform.position - transform.position;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector2 move = Vector2.zero;
        bool jump = false, charging = false;
        var k = Keyboard.current;
        if (k != null)
        {
            move += new Vector2((k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f), (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f));
            jump |= k.spaceKey.wasPressedThisFrame;
            charging |= k.eKey.isPressed;
        }
        var g = Gamepad.current;
        if (g != null)
        {
            var s = g.leftStick.ReadValue();
            if (s.magnitude > 0.2f) move += s;
            jump |= g.buttonSouth.wasPressedThisFrame;
            charging |= g.rightTrigger.ReadValue() > 0.5f;
        }
        bool manual = move.sqrMagnitude > 0.01f || jump || charging;
        if (manual) manualTimer = 2f;

        var pos = transform.position;
        if (move.sqrMagnitude > 0.01f)
        {
            var dir = new Vector3(move.x, 0f, move.y).normalized;
            pos += dir * runSpeed * dt;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * dt);
        }
        else if (autoMove && (manualTimer -= dt) <= 0f)
        {
            float c = Mathf.Repeat(Time.time, Cycle);
            if (c < 1.5f) { Face(Vector3.right, dt); pos = Vector3.MoveTowards(pos, home + Vector3.right * runDistance * 0.5f, runSpeed * dt); }
            else if (c < 3f) { }
            else if (c < 4.5f) { Face(Vector3.left, dt); pos = Vector3.MoveTowards(pos, home - Vector3.right * runDistance * 0.5f, runSpeed * dt); }
            else if (c < 6f) { }
            else if (c < 7f) transform.Rotate(0f, turnSpeed * dt, 0f);
            else if (c < 8f) { Face(Vector3.right, dt); if (!autoJumped) { jump = true; autoJumped = true; } }
            else if (c < 11f) { autoJumped = false; Face(Vector3.right, dt); charging = true; }
        }

        // 溜め：押している間に増え、離すとすぐ 0（放つ）
        charge = charging ? Mathf.MoveTowards(charge, 1f, dt / Mathf.Max(chargeTime, 0.01f)) : 0f;
        if (cape != null) cape.Charge = charge;

        // ジャンプ
        if (jump && pos.y <= home.y + 0.001f) velY = jumpPower;
        velY -= 15f * dt;
        pos.y = Mathf.Max(home.y, pos.y + velY * dt);
        if (pos.y <= home.y) velY = Mathf.Max(velY, 0f);
        transform.position = pos;
    }

    void LateUpdate()
    {
        if (followCamera == null) return;
        var target = new Vector3(transform.position.x, home.y, transform.position.z) + camOffset;
        followCamera.transform.position = Vector3.Lerp(followCamera.transform.position, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
    }

    void Face(Vector3 dir, float dt) =>
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * dt);
}
