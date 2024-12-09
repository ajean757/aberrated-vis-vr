using UnityEngine;

public class StickyUI : MonoBehaviour
{
    public Transform cameraTransform; // Drag your Main Camera here
    public Vector3 offset = new Vector3(0f, 0f, 0f); // Adjust for position in front of face

    void LateUpdate()
    {
        if (cameraTransform != null)
        {
            // Position the UI in front of the camera
            transform.position = cameraTransform.position + cameraTransform.TransformVector(offset);

            // Make the UI face the camera
            transform.rotation = Quaternion.LookRotation(transform.position - cameraTransform.position);

        }
    }
}
