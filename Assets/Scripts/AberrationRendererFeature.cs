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
  
    //[SerializeField] private Shader shader;
    [SerializeField] private ComputeShader computeShader;
    //private Material material;
    private AberrationRenderPass aberrationRenderPass;

    public override void Create()
    {
    // if (shader == null)
    // {
    //     return;
    // }
    // material = new Material(shader);
    //aberrationRenderPass = new AberrationRenderPass(material, settings, computeShader);
    settings.PSFSet = PSFSet;

        aberrationRenderPass = new AberrationRenderPass(settings, computeShader);
        aberrationRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(aberrationRenderPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
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
}

[Serializable]
public class AberrationSettings
{
  public string PSFSet;
}