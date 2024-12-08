using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class RaycastLogger : MonoBehaviour
{
    private XRRayInteractor rayInteractor;

    void Start()
    {
        rayInteractor = GetComponent<XRRayInteractor>();
    }

    void Update()
    {
        if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            Debug.Log($"Ray Hit: {hit.collider.gameObject.name}");
        }
        else
        {
            Debug.Log("No hit detected.");
        }
    }
}
