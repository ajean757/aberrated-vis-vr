using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class PSFSelectListener : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public TMP_Dropdown dropdown;
    public AberrationRendererFeature apertureFeature;
    void Start()
    {
        // Ensure Dropdown value change triggers scene loading
        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
    }

    private void OnDropdownValueChanged(int index)
    {
        switch (index)
        {
            case 0:
                apertureFeature.UpdateAberration(Camera.StereoscopicEye.Left, "Assets/Aberrations/healthy-binary");
                break;
            case 1:
                apertureFeature.UpdateAberration(Camera.StereoscopicEye.Left, "Assets/Aberrations/myopia-binary");
                break;
            case 2:
                apertureFeature.UpdateAberration(Camera.StereoscopicEye.Left, "Assets/Aberrations/astigmatism-binary");
                break;
            default:
                Debug.LogWarning("Invalid dropdown index!");
                break;
        }
    }
}
