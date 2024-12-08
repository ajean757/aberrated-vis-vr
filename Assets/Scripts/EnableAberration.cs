using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class AberrationControl : MonoBehaviour
{
    public Toggle aberrationToggle; // Assign the Toggle in the Inspector
    public ScriptableRendererFeature aberrationFeature; // Assign your renderer feature in the Inspector

    void Start()
    {
        // Add listener to the toggle to handle changes
        aberrationToggle.onValueChanged.AddListener(ToggleAberration);

        // Initialize the toggle's state
        ToggleAberration(aberrationToggle.isOn);
    }

    void ToggleAberration(bool isOn)
    {
        if (aberrationFeature != null)
        {
            aberrationFeature.SetActive(isOn);
        }
        else
        {
            Debug.LogWarning("Aberration Feature is not assigned.");
        }
    }
}
