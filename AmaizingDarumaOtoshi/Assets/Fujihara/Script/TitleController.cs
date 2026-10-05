using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

public class TitleController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (IsPressed())
        {
            Fader.Load("TestMenu");
        }
    }

    bool IsPressed()
    {
        foreach (var pad in Gamepad.all)
        {
            if (pad.buttonSouth.wasPressedThisFrame) return true;
        }

        var key = Keyboard.current;
        if (key != null && key.enterKey.wasPressedThisFrame) return true;

        return false;
    }
}
