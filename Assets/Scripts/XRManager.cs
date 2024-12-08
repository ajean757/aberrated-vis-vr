using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

public class XRManager : MonoBehaviour
{
    public bool useXR;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (useXR && Application.isPlaying)
				{
            StopXR();
            StartCoroutine(StartXRCoroutine());
				}
    }

    public bool UsingXR()
		{
        return Application.isPlaying && useXR;
		}

    IEnumerator StartXRCoroutine()
    {
        Debug.Log("Initializing XR...");
        yield return XRGeneralSettings.Instance.Manager.InitializeLoader();

        if (XRGeneralSettings.Instance.Manager.activeLoader == null)
        {
            Debug.LogError("Initializing XR Failed. Check Editor or Player log for details.");
        }
        else
        {
            Debug.Log("Starting XR...");
            XRGeneralSettings.Instance.Manager.StartSubsystems();
        }
    }

    void StopXR()
    {
        Debug.Log("Stopping XR...");

        XRGeneralSettings.Instance.Manager.StopSubsystems();
        XRGeneralSettings.Instance.Manager.DeinitializeLoader();
        Debug.Log("XR stopped completely.");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
