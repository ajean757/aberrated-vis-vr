using UnityEngine;

public class CenterScreenRaycast : MonoBehaviour
{
    public Camera vrCamera; // Assign the VR camera
    public float maxDistance = 100f; // Maximum distance for the raycast
    public LayerMask raycastLayerMask; // Layer mask to control what the raycast can hit
    public GameObject hitMarkerPrefab; // Prefab for the hit marker (e.g., a sphere)

    private GameObject currentHitMarker; // The active hit marker instance
    public float centerScreenDepth; // Expose depth to other scripts
    public float GetCurrentDepth() => centerScreenDepth;
    public bool markerOn = false;
    void Update()
    {
        if (vrCamera == null)
        {
            Debug.LogError("VR Camera is not assigned.");
            return;
        }

        // Perform the raycast
        Ray ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);

        // Draw the ray in red
        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.red);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, maxDistance, raycastLayerMask))
        {
            // Log the depth (distance) of the first hit
            float depth = hitInfo.distance;
            Debug.Log($"Hit object: {hitInfo.collider.name}, Depth: {depth} units");
            centerScreenDepth = depth;
            // Place the hit marker at the hit point
            if (markerOn)
            {if (currentHitMarker == null && hitMarkerPrefab != null)
            {
                // Instantiate the marker if it doesn't exist
                currentHitMarker = Instantiate(hitMarkerPrefab, hitInfo.point, Quaternion.identity);
            }
            else if (currentHitMarker != null)
            {
                // Move the existing marker to the new hit point
                currentHitMarker.transform.position = hitInfo.point;
            }} else
            {
                if (currentHitMarker != null)
            {
                Destroy(currentHitMarker);
            }
            }

            // Optionally draw the hit point marker in green (visual debug)
            Debug.DrawRay(hitInfo.point, Vector3.up * 0.1f, Color.green);
        }
        else
        {
            // Debug.Log("No hit detected.");
            // No hit: Set depth to infinity 
            centerScreenDepth = float.PositiveInfinity;
            // Remove the hit marker if nothing is hit
            if (currentHitMarker != null)
            {
                Destroy(currentHitMarker);
            }
        }
        // computeShader.SetFloat("_CenterScreenDepth", GetCurrentDepth());

    }
}
