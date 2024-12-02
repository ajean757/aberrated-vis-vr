using UnityEngine;
using UnityEngine.XR;

[ExecuteAlways] 
public class XRResolutionScaler : MonoBehaviour
{
    [Range(0.1f, 1.0f)]
    public float resolutionScale = 1.0f; 

    void Update()
    {
        // Apply the slider value to the XR resolution scale
        if (XRSettings.enabled && XRSettings.eyeTextureResolutionScale != resolutionScale)
        {
            XRSettings.eyeTextureResolutionScale = resolutionScale;
        }
    }
}
