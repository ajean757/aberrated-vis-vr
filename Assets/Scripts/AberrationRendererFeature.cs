using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class AberrationRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private AberrationSettings settings;
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
    [Tooltip("Size of a tile in pixels")]
    public int tileSize;
}