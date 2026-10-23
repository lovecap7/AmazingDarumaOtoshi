using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class NetCubeMover : NetworkBehaviour
{
    [SerializeField] float m_speed = 5f;
    Vector2 m_move; // ホスト側で保持する、最新の入力

    // Update is called once per frame
    void Update()
    {
        if(IsOwner)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            var v = Vector2.zero;
            if (kb.wKey.isPressed) v.y += 1;
            if (kb.aKey.isPressed) v.x -= 1;
            if (kb.sKey.isPressed) v.y -= 1;
            if (kb.dKey.isPressed) v.x += 1;
            SubmitMoveRpc(v);

        }

        if(IsServer)
        {
            transform.position += new Vector3(m_move.x, 0, m_move.y) * (m_speed * Time.deltaTime);
        }
    }

    [Rpc(SendTo.Server)]
    void SubmitMoveRpc(Vector2 move)
    {
        m_move = Vector2.ClampMagnitude(move, 1f);
    }
}
