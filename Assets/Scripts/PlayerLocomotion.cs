using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLocomotion : MonoBehaviour
{
    public InputActionAsset actionAsset; // Drag your InputAction asset here
    public float moveSpeed = 5f; // Adjust movement speed as needed
    public float rotationSpeed = 40f; // Adjust rotation speed as needed
    public float pitchClampMin = -90f; // Minimum pitch (looking down limit)
    public float pitchClampMax = 90f;  // Maximum pitch (looking up limit)

    private InputAction moveAction;
    private InputAction turnAction;
    private Transform cameraTransform;

    private float pitch = 0f; // Stores the up/down rotation value

    void Awake()
    {
        // Fetch the movement and turn actions from the action asset
        moveAction = actionAsset.FindActionMap("Default").FindAction("Move");
        turnAction = actionAsset.FindActionMap("Default").FindAction("Turn");

        // Find the main camera transform for pitch rotation
        cameraTransform = Camera.main.transform;
    }

    void OnEnable()
    {
        moveAction.Enable();
        turnAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        turnAction.Disable();
    }

    void Update()
    {
        // Handle movement input
        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        //Debug.Log("translation: " + moveInput);
        if ((moveInput.x * moveInput.x + moveInput.y * moveInput.y) > (0.12 * 0.12)) {
            Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);
            Vector3 worldMove = transform.TransformDirection(move) * moveSpeed * Time.deltaTime;
            transform.position += worldMove;
        }


        // Handle rotation input
        Vector2 turnInput = turnAction.ReadValue<Vector2>();
        //Debug.Log("rotation: " + turnInput);

        if ((turnInput.x * turnInput.x + turnInput.y * turnInput.y) > (0.12 * 0.12))
        {
            // Horizontal rotation (yaw)
            float yaw = turnInput.x * rotationSpeed * Time.deltaTime;

            pitch -= turnInput.y * rotationSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, pitchClampMin, pitchClampMax);

            // Apply both yaw and pitch to the transform
            transform.localRotation = Quaternion.Euler(pitch, transform.localEulerAngles.y + yaw, 0);
        }
    }
}
