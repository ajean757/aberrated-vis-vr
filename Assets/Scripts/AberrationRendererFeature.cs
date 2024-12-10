using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class FolderDropdownAttribute : Attribute
{
    // You can add parameters to this attribute if needed, e.g., to specify folder paths or other options.
}

public class AberrationRendererFeature : ScriptableRendererFeature
{
    private AberrationSettings settings;

    [FolderDropdown]
    public string PSFSet;

    [FolderDropdown]
    public string RightPSFSet;

    //[SerializeField] private Shader shader;
    [SerializeField] private ComputeShader computeShader;
    //private Material material;
    private AberrationRenderPass aberrationRenderPass;
    [Delayed]
    public float depth;
    [Delayed]
    public float aperture;
    [Delayed]
    public int mergePasses;

    public override void Create()
    {
        // if (shader == null)
        // {
        //     return;
        // }
        // material = new Material(shader);
        //aberrationRenderPass = new AberrationRenderPass(material, settings, computeShader);
        settings = new();
        settings.PSFSet = PSFSet;
        settings.RightPSFSet = RightPSFSet;
        aberrationRenderPass = new AberrationRenderPass(settings, computeShader);
        aberrationRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        aberrationRenderPass.SetDepth(depth);
        aberrationRenderPass.SetAperture(aperture);
        aberrationRenderPass.mergePasses = mergePasses;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(aberrationRenderPass);
        }
    }

    public AberrationRenderPass GetRenderPass()
    {
        return aberrationRenderPass;
    }

    protected override void Dispose(bool disposing)
    {
        aberrationRenderPass.Dispose();
        // #if UNITY_EDITOR
        //     if (EditorApplication.isPlaying)
        //     {
        //         Destroy(material);
        //     }
        //     else
        //     {
        //         DestroyImmediate(material);
        //     }
        // #else
        //         Destroy(material);
        // #endif
    }

    public void UpdateDepth(float newDepth)
    {
        depth = newDepth;
        aberrationRenderPass?.SetDepth(depth);
    }
    public void UpdateAperture(float newAperture)
    {
        aperture = newAperture;
        aberrationRenderPass?.SetAperture(aperture);
    }

    public void UpdateAberration(Camera.StereoscopicEye eye, string psfSetName)
		{
        if (eye == Camera.StereoscopicEye.Left)
				{
            settings.PSFSet = psfSetName;
				}
        else
				{
            settings.RightPSFSet = psfSetName;
				}
        aberrationRenderPass.psfStack = null;
        aberrationRenderPass.rightPsfStack = null;
        aberrationRenderPass.UpdateAberrationSettings(settings);
        aberrationRenderPass?.SetParams(aberrationRenderPass.resolution, true);
		}

    public void UpdateMergePasses(int mergePasses)
		{
        this.mergePasses = mergePasses;
        if (aberrationRenderPass != null)
            aberrationRenderPass.mergePasses = mergePasses;
		}
}

[Serializable]
public class AberrationSettings
{
    public string PSFSet;
    public string RightPSFSet;
}