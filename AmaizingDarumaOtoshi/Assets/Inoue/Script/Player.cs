using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    private const float kSpeed = 8f;
    private const float kJumpPower = 5f;
    private Vector2 m_moveInput = Vector2.zero;
    bool m_isJump = false;

    //積み木をまとめる親
    private Transform m_tumikis;

    //ハンマー
    private GameObject m_hammer;
    //ハンマーの全体フレーム
    private const float kAttackTime = 0.2f;
    private float m_animTime = 0.0f;
    private bool m_isAttack = false;

    private Vector3 m_hammerStartOffset;
    private Quaternion m_hammerStartRotation;
    // ハンマーの待機位置
    private const float kHammerRight = -1.1f;
    private const float kHammerHeight = 0.5f;
    private Vector3 m_hammerStartPosition;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_tumikis = transform.Find("Tumikis");
        m_hammer = GameObject.Find("Hammer").gameObject;
        m_hammerStartPosition = m_hammer.transform.position;
        m_hammerStartRotation = m_hammer.transform.rotation;
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 moveDirection = new Vector3(
        m_moveInput.x,
        0f,
        m_moveInput.y
        );

        if (!m_isAttack)
        {
            // 移動
            transform.Translate(
                moveDirection * (kSpeed - (m_tumikis.childCount - 1) * 0.6f) * Time.deltaTime,
                Space.World
            );

            // 移動しているときだけ向きを変える
            if (moveDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);

                // 滑らかに回転
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    10f * Time.deltaTime
                );
            }
        }

        UpdateTumiki();
        if (!m_isAttack)
        {
            UpdateHammerIdle();
        }
        else
        {
            //ハンマー
            UpdateAnimAttack();
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        m_moveInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        // 押された瞬間でPerformedとなる
        if (!context.performed) return;
        if (m_isJump) return;
        transform.GetComponent<Rigidbody>().AddForce(0.0f, kJumpPower, 0.0f, ForceMode.Impulse);
        m_isJump = true;
    }

    public void OnMeleeAttack(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if (m_isAttack) return;

        m_isAttack = true;
        m_animTime = 0.0f;

        // プレイヤーから見たハンマーの位置
        // プレイヤーのローカル座標でハンマーの開始位置を保存
        m_hammerStartOffset = transform.InverseTransformPoint(
            m_hammer.transform.position
        );

        // 通常時のハンマーの姿勢
        m_hammerStartRotation =
            m_hammer.transform.rotation;

        m_hammerStartPosition = m_hammer.transform.position;
    }
    public void OnShot(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        // 自分の積み木がなければ何もしない
        if (m_tumikis.childCount <= 0) return;

        // 一番上の積み木を取得
        Transform tumiki = m_tumikis.GetChild(m_tumikis.childCount - 1);

        // Tumikisから外す
        tumiki.SetParent(null);

        // コリジョンを戻す
        CapsuleCollider collider = tumiki.GetComponent<CapsuleCollider>();
        collider.isTrigger = false;

        // 重力を戻す
        Rigidbody rb = tumiki.GetComponent<Rigidbody>();
        rb.useGravity = true;

        // プレイヤーの前方へ飛ばす
        rb.AddForce(
            transform.forward * 5.0f + Vector3.up * 2.0f,
            ForceMode.Impulse
        );
    }
    private void OnCollisionEnter(Collision collision)
    {
        //ステージと衝突したとき
        if(collision.gameObject.CompareTag("Stage") && m_isJump)
        {
            m_isJump = false;
        }

        //積み木以外なら
        if (!collision.gameObject.CompareTag("Tumiki"))return;

        Transform tumiki = collision.transform;

        // すでに自分の子なら何もしない
        if (tumiki.IsChildOf(transform))return;

        if (!m_isJump) return;
        Rigidbody rb = GetComponent<Rigidbody>();
        Vector3 velocity = rb.linearVelocity;
        if (velocity.y > 1f) return;
        // 上から乗った場合のみ
        AddTumiki(tumiki);
    }

    private void AddTumiki(Transform tumiki)
    {
        //自身の座標を積み木に
        transform.position = tumiki.position;

        // Playerの子にする
        tumiki.SetParent(m_tumikis);
        tumiki.SetAsFirstSibling();

        //上限を超えたとき
        if (m_tumikis.childCount > 11)
        {
            // 一番後ろの積み木を取得
            Transform oldTumiki = m_tumikis.GetChild(m_tumikis.childCount - 2);

            // Tumikisから外す
            oldTumiki.SetParent(null);

            //少し離す
            oldTumiki.transform.localPosition += -transform.forward;

            // コリジョンを元に戻す
            CapsuleCollider collider = oldTumiki.GetComponent<CapsuleCollider>();
            collider.isTrigger = false;

            // 重力を元に戻す
            Rigidbody rb = oldTumiki.GetComponent<Rigidbody>();
            rb.useGravity = true;

            // 後ろ方向へ少し飛ばす
            rb.AddForce(-transform.forward * 3f + Vector3.up * -2.0f, ForceMode.Impulse);
        }

        //積み木のコリジョンをOFF
        tumiki.GetComponent<CapsuleCollider>().isTrigger = true;
        tumiki.GetComponent<Rigidbody>().useGravity = false;

    }

    private void UpdateTumiki()
    {
        for (int i = 0; i < m_tumikis.childCount; i++)
        {
            float height = 0.5f;
            Transform child = m_tumikis.GetChild(i);
            child.position = transform.position;
            var pos = child.position;
            pos.y += i * height;
            if (m_tumikis.childCount - 1 <= i)
            {
                pos.y += height * 0.5f;
            }
            child.position = pos;
        }
    }
    private void UpdateHammerIdle()
    {
        // プレイヤーの正面右
        Vector3 offset = transform.right * kHammerRight;

        offset.y += kHammerHeight;

        m_hammer.transform.position =
            transform.position + offset;

        // 通常時のハンマーの目標姿勢
        Quaternion targetRotation =
            Quaternion.Euler(0.0f, transform.eulerAngles.y, 0.0f);

        // 徐々に通常姿勢へ戻す
        m_hammer.transform.rotation = Quaternion.Slerp(
            m_hammer.transform.rotation,
            targetRotation,
            15.0f * Time.deltaTime
        );
    }
    //ハンマーで攻撃
    private void UpdateAnimAttack()
    {
        m_animTime += Time.deltaTime;

        // 0～1
        float t = Mathf.Clamp01(m_animTime / kAttackTime);

        // 0～180度
        float angle = 180.0f * t;

        // プレイヤーの向きを基準にしたハンマー開始位置
        Vector3 startOffset =
            transform.rotation * m_hammerStartOffset;

        // 高さ方向を無視する
        startOffset.y = 0.0f;

        // プレイヤーを中心にXZ平面上で180度円運動
        Vector3 offset =
            Quaternion.AngleAxis(angle, Vector3.up) * startOffset;

        // ハンマーの高さを常に一定にする
        offset.y = kHammerHeight;

        // ハンマーの位置
        m_hammer.transform.position =
            transform.position + offset;


        //========================================
        // ハンマーの向き
        //========================================

        Vector3 direction =
            transform.position - m_hammer.transform.position;

        // 高さ方向を無視
        direction.y = 0.0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            // 持ち手がプレイヤー側を向く
            m_hammer.transform.rotation =
                Quaternion.LookRotation(direction, Vector3.up)
                * Quaternion.Euler(-90.0f, 0.0f, 0.0f);
        }


        //========================================
        // 攻撃終了
        //========================================

        if (t >= 1.0f)
        {
            m_isAttack = false;
            m_animTime = 0.0f;

            // 通常位置へ戻す
            m_hammer.transform.position = m_hammerStartPosition;

            // 通常の姿勢へ戻す
            m_hammer.transform.rotation =
                m_hammerStartRotation;
        }
    }
}
