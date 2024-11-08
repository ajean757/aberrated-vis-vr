using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

public class AberrationRenderPass : ScriptableRenderPass
{
    // Defined consts in compute shader
    private const int TILE_SIZE = 16;
    private const int TILE_MAX_FRAGMENTS = 8192;

    // Struct sizes in compute shader
    //private const int TileSplatParamsSize = 2 * 16;
    private const int FragmentDataSize = 7 * sizeof(float);
    private const int TileFragmentCountSize = sizeof(uint);
    private const int SortIndexSize = sizeof(uint) + sizeof(float);

    // Params
    static readonly int NumTiles = Shader.PropertyToID("numTiles");
    static readonly int Resolution = Shader.PropertyToID("resolution");
    static readonly int MaxBlurRadiusCurrent = Shader.PropertyToID("maxBlurRadiusCurrent");

    // Buffers
    static readonly int FragmentBuffer = Shader.PropertyToID("fragmentBuffer");
    private GraphicsBuffer fragmentBuffer = null;

    static readonly int TileFragmentCountBuffer = Shader.PropertyToID("tileFragmentCountBuffer");
    private GraphicsBuffer tileFragmentCountBuffer = null;

    static readonly int TileSortBuffer = Shader.PropertyToID("tileSortBuffer");
    private GraphicsBuffer tileSortBuffer = null;

    // Textures
    static readonly int IColor = Shader.PropertyToID("iColor");
    static readonly int OColor = Shader.PropertyToID("oColor");
    static readonly int IDepth = Shader.PropertyToID("iDepth");

    private Vector2Int numTiles = Vector2Int.zero;
    private Vector2Int resolution = Vector2Int.zero;

    private ComputeShader cs;

    private AberrationSettings defaultSettings;

    //private GraphicsBuffer paramsBuffer;

    private GraphicsBuffer indicesBuffer;
    private GraphicsBuffer depthsBuffer;

    private GraphicsBuffer psfParamsBuffer;
    private GraphicsBuffer psfWeightsBuffer;

    private PSFStack psfStack;

    public AberrationRenderPass(AberrationSettings defaultSettings, ComputeShader cs)
    {
        this.defaultSettings = defaultSettings;
        this.cs = cs;
        
        //fragmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, , FragmentDataSize);
        //fragmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 100, Marshal.SizeOf(typeof(SplatIndex)));
        //paramsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, TileSplatParamsSize);

        // tileSplatBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 100, Marshal.SizeOf(typeof(SplatIndex)));
        // paramsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, TileSplatParamsSize);
        // use dynamic for now since we want to write to it a lot. see: https://docs.unity3d.com/ScriptReference/ComputeBufferMode.html
        indicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1024, sizeof(uint));
        indicesBuffer.name = "Indices";
        depthsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1024, sizeof(float));
        depthsBuffer.name = "Depths";

        // read in PSFs
        psfStack = new();
        Debug.Log("PSF set name: " + defaultSettings.PSFSet);
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
        public List<(int id, BufferHandle bufferhandle, AccessFlags accessFlags)> bufferList;
        public List<(int id, TextureHandle textureHandle, AccessFlags accessFlags)> textureList;
        public Vector3Int threadGroups;

        public void Build(IComputeRenderGraphBuilder builder)
        {
            foreach (var (_, bufferHandle, accessFlag) in bufferList)
                builder.UseBuffer(bufferHandle, accessFlag);
            // not sure what the difference between UseTexture and UseGlobalTexture is
            foreach (var (_, textureHandle, accessFlag) in textureList)
                builder.UseTexture(textureHandle, accessFlag);
        }
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        /*
        uint[] rangeArray = new uint[1024];
        float[] depthsArray = new float[1024];
        indicesBuffer.GetData(rangeArray);
        depthsBuffer.GetData(depthsArray);

        string rangeStr = "index after running SortFragments: ";
        foreach (var item in rangeArray)
        {
          rangeStr += item.ToString() + " ";
        }

        string depthsStr = "depths after running SortFragments: ";
        foreach (var item in depthsArray)
        {
          depthsStr += item.ToString() + " ";
        }
        Debug.Log(rangeStr);
        Debug.Log(depthsStr);
        Debug.Log("SortFragments End");
        */
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
        // (x - 1) / y + 1 is ceil(x / y) for ints
        // Vector2Int numTiles = new((cameraData.cameraTargetDescriptor.width - 1) / TILE_SIZE + 1, (cameraData.cameraTargetDescriptor.height - 1) / TILE_SIZE + 1);
        // uint[] arr = { (uint)numTiles.x, (uint)numTiles.y, (uint)cameraData.cameraTargetDescriptor.width, (uint)cameraData.cameraTargetDescriptor.height };
        // paramsBuffer.SetData(arr);
        // cs.SetConstantBuffer("TileSplatParams", paramsBuffer, 0, TileSplatParamsSize);
        //cs.SetVector("")
        
        // Update cbuffer values
        if (resolution != new Vector2Int(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height))
        {
            if (fragmentBuffer != null)
                fragmentBuffer.Release();
            if (tileFragmentCountBuffer != null)
                tileFragmentCountBuffer.Release();
            if (tileSortBuffer != null)
                tileSortBuffer.Release();

            resolution = new Vector2Int(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
            numTiles = new Vector2Int((resolution.x - 1) / TILE_SIZE + 1, (resolution.y - 1) / TILE_SIZE + 1);
            cs.SetInts(Resolution, new[] { resolution.x, resolution.y });
            cs.SetInts(NumTiles, new[] { numTiles.x, numTiles.y });

            fragmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, resolution.x * resolution.y, FragmentDataSize);
            tileFragmentCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numTiles.x * numTiles.y, TileFragmentCountSize);
            tileSortBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numTiles.x * numTiles.y * TILE_MAX_FRAGMENTS, SortIndexSize);
        }

        // This check is to avoid an error from the material preview in the scene
        if (resolution.x == 0 || !srcCamColor.IsValid() || !dst.IsValid())
            return;

        BufferHandle fragmentHandle = renderGraph.ImportBuffer(fragmentBuffer);
        BufferHandle tileFragmentCountHandle = renderGraph.ImportBuffer(tileFragmentCountBuffer);
        BufferHandle tileSortHandle = renderGraph.ImportBuffer(tileSortBuffer);

        using (var builder = renderGraph.AddComputePass("TileBufferBuild", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelName = "TileBufferBuild";
            passData.bufferList = new()
            {
                (FragmentBuffer, fragmentHandle, AccessFlags.Write),
                (TileFragmentCountBuffer, tileFragmentCountHandle, AccessFlags.Write),
                (TileSortBuffer, tileSortHandle, AccessFlags.Write),
            };
            passData.textureList = new() 
            {
                (IColor, srcCamColor, AccessFlags.Read),
                (IDepth, srcCamDepth, AccessFlags.Read),
            };
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        using (var builder = renderGraph.AddComputePass("TileBufferSplat", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelName = "TileBufferSplat";
            passData.bufferList = new()
            {
                (FragmentBuffer, fragmentHandle, AccessFlags.Read),
                (TileFragmentCountBuffer, tileFragmentCountHandle, AccessFlags.ReadWrite),
                (TileSortBuffer, tileSortHandle, AccessFlags.Write),
            };
            passData.textureList = new();
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        using (var builder = renderGraph.AddComputePass("BlurTest", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelName = "BlurTest";
            passData.bufferList = new();
            passData.textureList = new()
            {
                (IColor, srcCamColor, AccessFlags.Read),
                (OColor, dst, AccessFlags.Write),
                (IDepth, srcCamDepth, AccessFlags.Read),
            };
            passData.threadGroups = new(Mathf.CeilToInt(cameraData.cameraTargetDescriptor.width / 8), Mathf.CeilToInt(cameraData.cameraTargetDescriptor.height / 8), 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        /*
        using (var builder = renderGraph.AddComputePass("SortFragments", out PassData passData))
        {

          BufferHandle indicesBufferHandle = renderGraph.ImportBuffer(indicesBuffer);
          BufferHandle depthsBufferHandle = renderGraph.ImportBuffer(depthsBuffer);

          passData.cs = cs;
          passData.kernelName = "SortFragments";
          passData.bufferList = new()
          {
            ("indices", indicesBufferHandle, AccessFlags.ReadWrite),
            ("depths", depthsBufferHandle, AccessFlags.ReadWrite)
          };
          passData.textureList = new();
          passData.threadGroups = new(1, 1, 1);

          foreach (var (_, bufferHandle, accessFlag) in passData.bufferList)
            builder.UseBuffer(bufferHandle, accessFlag);
          // not sure what the difference between UseTexture and UseGlobalTexture is
          foreach (var (_, textureHandle, accessFlag) in passData.textureList)
            builder.UseTexture(textureHandle, accessFlag);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => {
            List<int> range = Enumerable.Range(0, 1024).ToList();
            List<float> randomDepths = Enumerable.Range(0, 1024).Select(_ => Random.Range(0.0f, 1.0f)).ToList();

            Debug.Log("SortFragments Start");

            string rangeStr;
            string depthsStr;

            rangeStr = "index before running compute shader: ";
            foreach (var item in range)
            {
              rangeStr += item.ToString() + " ";
            }

            depthsStr = "depths before running compute shader: ";
            foreach (var item in randomDepths)
            {
              depthsStr += item.ToString() + " ";
            }
            Debug.Log(rangeStr);
            Debug.Log(depthsStr);

        indicesBuffer.SetData(range);
                depthsBuffer.SetData(randomDepths);

                ExecutePass(data, cgContext);

              });
            }
        */

        // using (var builder = renderGraph.AddComputePass("Convolution", out PassData passData))
        // {
        //     passData.cs = cs;
        //     passData.kernelName = "Convolution";
        //     passData.bufferList = new() 
        //     { 
        //         ("_oTileSplatData", tileSplatHandle, AccessFlags.ReadWrite)  //todo
        //     };
        //     passData.textureList = new() 
        //     { 
        //         ("_iColor", srcCamColor, AccessFlags.Read), //todo
        //         ("_iDepth", srcCamDepth, AccessFlags.Read)  //todo
        //     };
        //     passData.threadGroups = new(1, 1, 1);

        //     foreach (var (_, bufferHandle, accessFlag) in passData.bufferList)
        //         builder.UseBuffer(bufferHandle, accessFlag);
        //     // not sure what the difference between UseTexture and UseGlobalTexture is
        //     foreach (var (_, textureHandle, accessFlag) in passData.textureList)
        //         builder.UseTexture(textureHandle, accessFlag);
        //     builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        // }

        resourceData.cameraColor = dst;
    }

    static void ExecutePass(PassData data, ComputeGraphContext cgContext)
    {
        int kernelIndex = data.cs.FindKernel(data.kernelName);
        foreach (var (id, bufferHandle, _) in data.bufferList)
            cgContext.cmd.SetComputeBufferParam(data.cs, kernelIndex, id, bufferHandle);
        foreach (var (id, textureHandle, _) in data.textureList)
            cgContext.cmd.SetComputeTextureParam(data.cs, kernelIndex, id, textureHandle);
        cgContext.cmd.DispatchCompute(data.cs, kernelIndex, data.threadGroups.x, data.threadGroups.y, data.threadGroups.z);
    }

    // No idea if this actually gets called
    public void Dispose()
    {
        fragmentBuffer.Release();
        tileFragmentCountBuffer.Release();
        tileSortBuffer.Release();
    }
}