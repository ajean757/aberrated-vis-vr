using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    public string scene;
    public string newScene;

    public string leftPSF;
    public string newLeftPSF;
    
    public string rightPSF;
    public string newRightPSF;

    public float aperture;
    public float newAperture;

    public float resolution;
    public float newResolution;
    
    public int mergePasses;
    public int newMergePasses;

    public GameObject menu;

    public InputActionReference menuAction;
    public InputActionReference moveAction;
    public InputActionReference navigateAction;
    public InputActionReference triggerAction;
    public InputActionReference triggerRightAction;

    public AberrationRendererFeature feature;

    public static UIManager Instance = null;

    public bool aberrationToggle;

    public bool ActiveAberration => !menu.activeSelf && aberrationToggle;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
				if (Instance != null && Instance != this)
				{
						Debug.Log("ui manager destroyed");
						Destroy(gameObject);
						return;
				}

				Instance = this;
				DontDestroyOnLoad(gameObject);

				if (menuAction != null)
				{
            menuAction.action.Enable();
            menuAction.action.performed += OnMenuButtonPressed;
        }

        if (triggerAction != null)
				{
            triggerAction.action.Enable();
            triggerAction.action.performed += OnTriggerPressed;
				}

        if (triggerRightAction != null)
				{
            triggerRightAction.action.Enable();
            triggerRightAction.action.performed += OnTriggerRightPressed;
				}

        resolution = 0.5f;
        menu.SetActive(false);
        aberrationToggle = true;

/*        transform.SetParent(Camera.main.transform);
        transform.localPosition = new Vector3(0, 0, 0.5f);*/
        //transform.parent = Camera.main.transform;

        scene = SceneManager.GetActiveScene().name;
        leftPSF = feature.PSFSet;
        rightPSF = feature.RightPSFSet;
    }

    private void OnMenuButtonPressed(InputAction.CallbackContext context)
    {
        if (menu.activeSelf)
        {
            Load();

            navigateAction.action.Disable();
            moveAction.action.Enable();
            menu.SetActive(false);
            feature.SetActive(ActiveAberration);
        }
        else
				{
            moveAction.action.Disable();
            navigateAction.action.Enable();
            menu.SetActive(true);
            feature.SetActive(ActiveAberration);

            newScene = scene;
            newLeftPSF = leftPSF;
            newRightPSF = rightPSF;
            newAperture = aperture;
            newResolution = resolution;
            newMergePasses = mergePasses;
				}
    }

    public void OnTriggerPressed(InputAction.CallbackContext context)
		{
        aberrationToggle = !aberrationToggle;
        feature.SetActive(ActiveAberration);
    }

    public void OnTriggerRightPressed(InputAction.CallbackContext context)
		{
        float l = feature.aberrationRenderPass.TileFragmentBufferOccupancy(0);
        float r = feature.aberrationRenderPass.TileFragmentBufferOccupancy(1);
        Debug.Log("Tile Fragment Buffer Occupancy: " + l.ToString() + " " + r.ToString());
		}

    // Update is called once per frame
    void Update()
    {
				if (XRSettings.enabled && XRSettings.eyeTextureResolutionScale != resolution)
				{
						XRSettings.eyeTextureResolutionScale = resolution;
						//Debug.Log("setting resolution scale to " + resolution.ToString());
				}
		}

    public void SelectScene(string name)
		{
        newScene = name;
		}

    public void SelectPSFLeft(string name)
		{
        newLeftPSF = name;
        //Debug.Log("select psf left: " + name);
		}

    public void SelectPSFRight(string name)
    {
        newRightPSF = name;
        //Debug.Log("select psf right: " + name);
    }

    public void SelectAperture(float val)
		{
        newAperture = val;
        //Debug.Log("aperture: " + val.ToString());
		}

    public void SelectResolution(float val)
		{
        newResolution = val;
		}

    public void SelectMergePasses(float val)
		{
        newMergePasses = (int)val;
		}

    public void Load()
		{
        Debug.Log("loading");
        resolution = newResolution;

        if (scene != newScene)
            SceneManager.LoadScene(scene = newScene);
        if (leftPSF != newLeftPSF)
            feature.UpdateAberration(Camera.StereoscopicEye.Left, leftPSF = newLeftPSF);
        if (rightPSF != newRightPSF)
            feature.UpdateAberration(Camera.StereoscopicEye.Right, rightPSF = newRightPSF);
        if (aperture != newAperture)
            feature.UpdateAperture(aperture = newAperture);
        if (mergePasses != newMergePasses)
            feature.UpdateMergePasses(mergePasses = newMergePasses);
    }
}
