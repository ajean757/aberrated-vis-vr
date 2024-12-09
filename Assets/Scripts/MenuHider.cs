using UnityEngine;
using UnityEngine.InputSystem;

public class ToggleGameObjectVisibility : MonoBehaviour
{
    public InputActionReference menuAction; // Drag your MenuAction here
    public GameObject parentObject; // Drag the parent GameObject here

    private bool isObjectVisible = true;

    private void OnEnable()
    {
        // Enable the input action and listen for it
        if (menuAction != null)
        {
            menuAction.action.Enable();
            menuAction.action.performed += OnMenuButtonPressed;
        }
    }

    private void OnDisable()
    {
        // Disable the input action and stop listening
        if (menuAction != null)
        {
            menuAction.action.performed -= OnMenuButtonPressed;
            menuAction.action.Disable();
        }
    }

    private void OnMenuButtonPressed(InputAction.CallbackContext context)
    {
/*        Debug.Log("Menu button pressed!");
*/        isObjectVisible = !isObjectVisible;

        if (parentObject != null)
        {
            parentObject.SetActive(isObjectVisible);
        }
        else
        {
            Debug.LogWarning("Parent object is not assigned!");
        }
    }
}
