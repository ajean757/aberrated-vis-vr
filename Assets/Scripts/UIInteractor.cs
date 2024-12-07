using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class SimpleUIInteractor : MonoBehaviour
{
    public InputActionAsset actionAsset; // Drag your InputAction asset here
    public LayerMask uiLayerMask; // Assign the UI layer for raycasting
    public Transform rayOrigin; // The origin of the ray (e.g., controller or camera)
    public float rayLength = 50f; // Maximum ray length

    private InputAction pointAction;
    private InputAction clickAction;

    void Awake()
    {
        // Fetch the input actions from the asset
        pointAction = actionAsset.FindActionMap("Default").FindAction("Point");
        clickAction = actionAsset.FindActionMap("Default").FindAction("Click");
    }

    void OnEnable()
    {
        pointAction.Enable();
        clickAction.Enable();
    }

    void OnDisable()
    {
        pointAction.Disable();
        clickAction.Disable();
    }

    void Update()
    {
        // Visualize the ray
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        Debug.Log($"Ray Origin Position: {rayOrigin.position}, Forward: {rayOrigin.forward}");
        Debug.DrawRay(rayOrigin.position, rayOrigin.forward * rayLength, Color.red);


        if (Physics.Raycast(ray, out RaycastHit hit, rayLength, uiLayerMask))
        {
            Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.green);


            // Check if the ray hit a UI element
            if (hit.collider != null && clickAction.WasPerformedThisFrame())
            {
                // Interact with UI
                var eventData = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(hit.collider.gameObject, eventData, ExecuteEvents.pointerClickHandler);
            }
        }
        else
        {
            Debug.DrawRay(ray.origin, ray.direction * rayLength, Color.red);
        }
    }
}
