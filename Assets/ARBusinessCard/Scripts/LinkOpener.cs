using UnityEngine;

public class LinkOpener : MonoBehaviour
{
    // Allows you to change the link directly in the Unity Inspector
    [SerializeField] private string url; 

    // Public method that will be linked to the button's click event
    public void OpenWebpage()
    {
        if (!string.IsNullOrEmpty(url))
        {
            Application.OpenURL(url);
        }
        else
        {
            Debug.LogWarning("URL is empty! Please assign a link in the Inspector.");
        }
    }
}
