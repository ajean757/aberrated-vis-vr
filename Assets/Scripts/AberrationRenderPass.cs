using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

public class AberrationRenderPass : ScriptableRenderPass
{
    private static readonly int horizontalBlurId = Shader.PropertyToID("_HorizontalBlur");
    private static readonly int verticalBlurId = Shader.PropertyToID("_VerticalBlur");
    private const string k_BlurTextureName = "_BlurTexture";
    private const string k_VerticalPassName = "VerticalBlurRenderPass";
    private const string k_HorizontalPassName = "HorizontalBlurRenderPass";

    private ComputeShader cs;

    private AberrationSettings defaultSettings;
    //private Material material;

    private RenderTextureDescriptor blurTextureDescriptor;

    GraphicsBuffer tileSplatBuffer;

    // public AberrationRenderPass(Material material, AberrationSettings defaultSettings, ComputeShader cs)
    public AberrationRenderPass(AberrationSettings defaultSettings, ComputeShader cs)
    {
        //this.material = material;
        this.defaultSettings = defaultSettings;
        this.cs = cs;

        blurTextureDescriptor = new RenderTextureDescriptor(Screen.width, Screen.height,
            RenderTextureFormat.Default, 0);

        tileSplatBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 100, System.Runtime.InteropServices.Marshal.SizeOf(typeof(SplatIndex)));
    }

    private void UpdateAberrationSettings()
    {
        // Read from defaultSettings


        //if (material == null) return;

        // Use the Volume settings or the default settings if no Volume is set.
        // var volumeComponent =
        //     VolumeManager.instance.stack.GetComponent<CustomVolumeComponent>();
        // float horizontalBlur = volumeComponent.horizontalBlur.overrideState ?
        //     volumeComponent.horizontalBlur.value : defaultSettings.horizontalBlur;
        // float verticalBlur = volumeComponent.verticalBlur.overrideState ?
        //     volumeComponent.verticalBlur.value : defaultSettings.verticalBlur;
        //material.SetFloat(horizontalBlurId, horizontalBlur);
        //material.SetFloat(verticalBlurId, verticalBlur);
    }

    // PassData is used to pass data when recording to the execution of the pass.
    class PassData
    {
        // Compute shader.
        public ComputeShader cs;
        // Buffer handles for the compute buffers.
        public string kernelName;
        public List<(string, BufferHandle, AccessFlags)> bufferList;
        public List<(string, TextureHandle, AccessFlags)> textureList;
        // need to add access flags
        public Vector3Int threadGroups;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph,
    ContextContainer frameData)
    {
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        // The following line ensures that the render pass doesn't blit
        // from the back buffer.
        if (resourceData.isActiveTargetBackBuffer)
            return;

        // Set the blur texture size to be the same as the camera target size.
        blurTextureDescriptor.width = cameraData.cameraTargetDescriptor.width;
        blurTextureDescriptor.height = cameraData.cameraTargetDescriptor.height;
        blurTextureDescriptor.depthBufferBits = 0;
        
        TextureHandle srcCamColor = resourceData.activeColorTexture;
        TextureHandle srcCamDepth = resourceData.activeDepthTexture;
        TextureHandle dst = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
            blurTextureDescriptor, k_BlurTextureName, false);

        // Update the blur settings in the material
        UpdateAberrationSettings();

        // This check is to avoid an error from the material preview in the scene
        if (!srcCamColor.IsValid() || !dst.IsValid())
            return;
        
        // The AddBlitPass method adds a vertical blur render graph pass that blits from the source texture (camera color in this case) to the destination texture using the first shader pass (the shader pass is defined in the last parameter).
        // RenderGraphUtils.BlitMaterialParameters paraVertical = new(srcCamColor, dst, material, 0);
        // renderGraph.AddBlitPass(paraVertical, k_VerticalPassName);
        
        // // The AddBlitPass method adds a horizontal blur render graph pass that blits from the texture written by the vertical blur pass to the camera color texture. The method uses the second shader pass.
        // RenderGraphUtils.BlitMaterialParameters paraHorizontal = new(dst, srcCamColor, material, 1);
        // renderGraph.AddBlitPass(paraHorizontal, k_HorizontalPassName);




        BufferHandle tileSplatHandle = renderGraph.ImportBuffer(tileSplatBuffer);

        using (var builder = renderGraph.AddComputePass("TileBufferBuild", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelName = "TileBufferBuild";
            passData.bufferList = new() { ("_oTileSplatData", tileSplatHandle, AccessFlags.ReadWrite) };
            passData.textureList = new() { ("_iColor", srcCamColor, AccessFlags.Read), ("_iDepth", srcCamDepth, AccessFlags.Read) };
            passData.threadGroups = new(1, 1, 1);

            foreach (var (_, bufferHandle, accessFlag) in passData.bufferList)
                builder.UseBuffer(bufferHandle, accessFlag);
            // not sure what the difference between UseTexture and UseGlobalTexture is
            foreach (var (_, textureHandle, accessFlag) in passData.textureList)
                builder.UseTexture(textureHandle, accessFlag);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }




    }

    static void ExecutePass(PassData data, ComputeGraphContext cgContext)
    {
        int kernelIndex = data.cs.FindKernel(data.kernelName);
        foreach (var (name, bufferHandle, _) in data.bufferList)
            cgContext.cmd.SetComputeBufferParam(data.cs, kernelIndex, name, bufferHandle);
        foreach (var (name, textureHandle, _) in data.textureList)
            cgContext.cmd.SetComputeTextureParam(data.cs, kernelIndex, name, textureHandle);
        cgContext.cmd.DispatchCompute(data.cs, kernelIndex, data.threadGroups.x, data.threadGroups.y, data.threadGroups.z);
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct SplatIndex
{
    public uint uiTileId;
    public uint uiFragmentIndex;
    public float fFragmentDepth;
    public float fBlurRadius;
    public Vector2 vScreenPosition;
}