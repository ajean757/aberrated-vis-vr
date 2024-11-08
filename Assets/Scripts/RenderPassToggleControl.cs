using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

public class RenderPassToggleControl : MonoBehaviour
{
    public Toggle passToggle; // Assign the Toggle in the Inspector
    public AberrationRendererFeature aberrationRendererFeature; // Reference to AberrationRendererFeature

    void Start()
    {
        // Set initial toggle state
        passToggle.isOn = true;

        // Add listener for toggle changes
        passToggle.onValueChanged.AddListener(OnBlurToggleChanged);

        // Set the initial blur state
        if (aberrationRendererFeature != null)
        {
            AberrationRenderPass aberrationRenderPass = aberrationRendererFeature.GetAberrationRenderPass();
            if (aberrationRenderPass != null)
            {
                aberrationRenderPass.enablePass = true;
            }
        }
    }

    void OnBlurToggleChanged(bool isOn)
    {
        // Update the render pass based on toggle state
        if (aberrationRendererFeature != null)
        {
            AberrationRenderPass aberrationRenderPass = aberrationRendererFeature.GetAberrationRenderPass();
            if (aberrationRenderPass != null)
            {
                aberrationRenderPass.enablePass = isOn;
            }
        }
    }
}
