using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the EventSystem object. Quits the app when the Android back
/// button is pressed (Unity reports it as the Escape key). Quit() is also
/// public so it can be wired to an Exit button's OnClick() if you want one.
/// </summary>
public class ApplicationQuitHandler : MonoBehaviour
{
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Quit();
        }
    }

    public void Quit()
    {
        Application.Quit();
    }
}
