using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.Rendering.DebugUI;

public class ApertureController : MonoBehaviour
{
    public Slider apertureSlider; // Reference to the slider in the UI
    public AberrationRendererFeature apertureFeature; // Reference to the render feature

    void Start()
    {
        if (apertureSlider != null)
        {
            // Set the slider's initial value to default 5
            apertureSlider.value = 5.0f;

            // Add a listener to update the aperture dynamically
            apertureSlider.onValueChanged.AddListener((value) =>
            {
                
                if (apertureFeature != null)
                {
                    apertureFeature.UpdateAperture(value);
                }
            });
        }
    }
    private void Update()
    {
        Debug.Log(apertureSlider.value.ToString());
    }
}
