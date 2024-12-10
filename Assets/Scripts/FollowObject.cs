using UnityEngine;

public class FollowObject : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (Camera.main != null)
            transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
    }
}
