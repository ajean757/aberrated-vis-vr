using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLocomotion : MonoBehaviour
{
  public InputActionAsset actionAsset; // Drag your InputAction asset here
  public float moveSpeed = 2f; // Adjust movement speed as needed
  public float rotationSpeed = 100f; // Adjust rotation speed as needed

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
    Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);
    Vector3 worldMove = transform.TransformDirection(move) * moveSpeed * Time.deltaTime;
    transform.position += worldMove;

    // Handle rotation input
    Vector2 turnInput = turnAction.ReadValue<Vector2>();

    // Debug.Log("Turning: " + turnInput.ToString());
    // Debug.Log("Translation: " + moveInput.ToString());

    if (turnInput != Vector2.zero)
    {
      // Horizontal rotation (yaw)
      float yaw = turnInput.x * rotationSpeed * Time.deltaTime;

      pitch -= turnInput.y * rotationSpeed * Time.deltaTime;

      // Apply both yaw and pitch to the transform
      transform.localRotation = Quaternion.Euler(pitch, transform.localEulerAngles.y + yaw, 0);
    }
  }
}