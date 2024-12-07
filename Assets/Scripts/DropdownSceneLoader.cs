using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class DropdownSceneLoader : MonoBehaviour
{
    public TMP_Dropdown dropdown;

    void Start()
    {
        // Ensure Dropdown value change triggers scene loading
        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
    }

    private void OnDropdownValueChanged(int index)
    {
        // Map dropdown index to scene names
        switch (index)
        {
            case 0:
                SceneManager.LoadScene("SampleScene"); // Replace with your scene names
                break;
            case 1:
                SceneManager.LoadScene("placeholder1"); // Replace with your scene names
                break;
            case 2:
                SceneManager.LoadScene("placeholder2"); // Replace with your scene names
                break;
            default:
                Debug.LogWarning("Invalid dropdown index!");
                break;
        }
    }

    void OnDestroy()
    {
        dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
    }
}
