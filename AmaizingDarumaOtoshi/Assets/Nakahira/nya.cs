using UnityEngine;

public class nya : MonoBehaviour
{
    Vector2 m_vel = new(10.0f, 10.0f);
    [SerializeField]
    Camera m_camera;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        transform.Translate(m_vel * Time.deltaTime);
        
        Vector2 screenPos = m_camera.WorldToScreenPoint(transform.position);

        if (screenPos.x < 0.0f || screenPos.x > Screen.width)
        {
            m_vel.x *= -1.0f; 
        }

        if (screenPos.y < 0.0f || screenPos.y > Screen.height)
        {
            m_vel.y *= -1.0f;
        }
    }
}
