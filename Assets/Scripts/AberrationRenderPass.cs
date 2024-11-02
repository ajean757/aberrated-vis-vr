using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class AberrationRenderPass : ScriptableRenderPass
{
    private static readonly int TileSplatParamsSize = 2 * sizeof(uint);

    private ComputeShader cs;

    private AberrationSettings defaultSettings;

    private GraphicsBuffer tileSplatBuffer;
    private GraphicsBuffer paramsBuffer;

    public AberrationRenderPass(AberrationSettings defaultSettings, ComputeShader cs)
    {
        this.defaultSettings = defaultSettings;
        this.cs = cs;

        tileSplatBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 100, Marshal.SizeOf(typeof(SplatIndex)));
        paramsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, TileSplatParamsSize);
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
        
        TextureHandle srcCamColor = resourceData.activeColorTexture;
        TextureHandle srcCamDepth = resourceData.activeDepthTexture;

        var dstDesc = renderGraph.GetTextureDesc(srcCamColor);
        dstDesc.name = "CameraColor-Aberration";
        dstDesc.clearBuffer = false;
        dstDesc.enableRandomWrite = true;
        TextureHandle dst = renderGraph.CreateTexture(dstDesc);

        // Update the blur settings in the material
        UpdateAberrationSettings();

        // change these so it only calls setdata and setconstantbuffer when parameters change
        uint[] arr = { (uint)cameraData.cameraTargetDescriptor.width, (uint)cameraData.cameraTargetDescriptor.height };
        paramsBuffer.SetData(arr);
        cs.SetConstantBuffer("TileSplatParams", paramsBuffer, 0, TileSplatParamsSize);

        // This check is to avoid an error from the material preview in the scene
        if (!srcCamColor.IsValid() || !dst.IsValid())
            return;

        BufferHandle tileSplatHandle = renderGraph.ImportBuffer(tileSplatBuffer);
        

        // using (var builder = renderGraph.AddComputePass("TileBufferBuild", out PassData passData))
        // {
        //     passData.cs = cs;
        //     passData.kernelName = "TileBufferBuild";
        //     passData.bufferList = new() 
        //     { 
        //         ("_oTileSplatData", tileSplatHandle, AccessFlags.ReadWrite) 
        //     };
        //     passData.textureList = new() 
        //     { 
        //         ("_iColor", srcCamColor, AccessFlags.Read), 
        //         ("_iDepth", srcCamDepth, AccessFlags.Read) 
        //     };
        //     passData.threadGroups = new(1, 1, 1);

        //     foreach (var (_, bufferHandle, accessFlag) in passData.bufferList)
        //         builder.UseBuffer(bufferHandle, accessFlag);
        //     // not sure what the difference between UseTexture and UseGlobalTexture is
        //     foreach (var (_, textureHandle, accessFlag) in passData.textureList)
        //         builder.UseTexture(textureHandle, accessFlag);
        //     builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        // }

        // using (var builder = renderGraph.AddComputePass("TileBufferSplat", out PassData passData))
        // {
        //     passData.cs = cs;
        //     passData.kernelName = "TileBufferSplat";
        //     passData.bufferList = new() 
        //     { 
        //         ("_oTileSplatData", tileSplatHandle, AccessFlags.ReadWrite) 
        //     };
        //     passData.textureList = new() 
        //     { 
        //         ("_iColor", srcCamColor, AccessFlags.Read), 
        //         ("_iDepth", srcCamDepth, AccessFlags.Read) 
        //     };
        //     passData.threadGroups = new(1, 1, 1);

        //     foreach (var (_, bufferHandle, accessFlag) in passData.bufferList)
        //         builder.UseBuffer(bufferHandle, accessFlag);
        //     // not sure what the difference between UseTexture and UseGlobalTexture is
        //     foreach (var (_, textureHandle, accessFlag) in passData.textureList)
        //         builder.UseTexture(textureHandle, accessFlag);
        //     builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        // }

        using (var builder = renderGraph.AddComputePass("BlurTest", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelName = "BlurTest";
            passData.bufferList = new();
            passData.textureList = new() 
            {
                ("_iColor", srcCamColor, AccessFlags.Read),
                ("_oColor", dst, AccessFlags.Write),
                ("_iDepth", srcCamDepth, AccessFlags.Read)
            };
            passData.threadGroups = new(Mathf.CeilToInt(cameraData.cameraTargetDescriptor.width / 8), Mathf.CeilToInt(cameraData.cameraTargetDescriptor.height / 8), 1);

            foreach (var (_, bufferHandle, accessFlag) in passData.bufferList)
                builder.UseBuffer(bufferHandle, accessFlag);
            // not sure what the difference between UseTexture and UseGlobalTexture is
            foreach (var (_, textureHandle, accessFlag) in passData.textureList)
                builder.UseTexture(textureHandle, accessFlag);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        resourceData.cameraColor = dst;
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

[StructLayout(LayoutKind.Sequential)]
struct SplatIndex
{
    public uint uiTileId;
    public uint uiFragmentIndex;
    public float fFragmentDepth;
    public float fBlurRadius;
    public Vector2 vScreenPosition;
}