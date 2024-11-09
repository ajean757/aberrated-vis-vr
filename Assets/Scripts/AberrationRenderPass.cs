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
    private static readonly int numTilesId = Shader.PropertyToID("numTiles");
    private static readonly int resolutionId = Shader.PropertyToID("resolution");
    private static readonly int maxBlurRadiusCurrentId = Shader.PropertyToID("maxBlurRadiusCurrent");

    // Kernels (initialized in constructor)
    private readonly int tileBufferBuildIndex;
    private readonly int tileBufferSplatIndex;
    private readonly int sortFragmentsIndex;
    private readonly int blurTestIndex;

    // Buffers
    private static readonly int fragmentBufferId = Shader.PropertyToID("fragmentBuffer");
    private GraphicsBuffer fragmentBuffer = null;

    private static readonly int tileFragmentCountBufferId = Shader.PropertyToID("tileFragmentCountBuffer");
    private GraphicsBuffer tileFragmentCountBuffer = null;

    private static readonly int tileSortBufferId = Shader.PropertyToID("tileSortBuffer");
    private GraphicsBuffer tileSortBuffer = null;

    // Textures
    private static readonly int iColorId = Shader.PropertyToID("iColor");
    private static readonly int oColorId = Shader.PropertyToID("oColor");
    private static readonly int iDepthId = Shader.PropertyToID("iDepth");

    // Runtime cbuffer values
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

        // Initialize kernel IDs
        tileBufferBuildIndex = cs.FindKernel("TileBufferBuild");
        tileBufferSplatIndex = cs.FindKernel("TileBufferSplat");
        sortFragmentsIndex = cs.FindKernel("SortFragments");
        blurTestIndex = cs.FindKernel("BlurTest");

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
    }

    // PassData is used to pass data when recording to the execution of the pass.
    class PassData
    {
        // Compute shader.
        public ComputeShader cs;
        // Buffer handles for the compute buffers.
        public int kernelIndex;
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
        // uint[] rangeArray = new uint[1024];
        // float[] depthsArray = new float[1024];
        // indicesBuffer.GetData(rangeArray);
        // depthsBuffer.GetData(depthsArray);

        // string rangeStr = "index after running SortFragments: ";
        // foreach (var item in rangeArray)
        // {
        //   rangeStr += item.ToString() + " ";
        // }

        // string depthsStr = "depths after running SortFragments: ";
        // foreach (var item in depthsArray)
        // {
        //   depthsStr += item.ToString() + " ";
        // }
        // Debug.Log(rangeStr);
        // Debug.Log(depthsStr);
        // Debug.Log("SortFragments End");
        
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

        // Update cbuffer values
        if (resolution != new Vector2Int(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height))
        {
            Debug.Log("Resolution changed");
            resolution = new Vector2Int(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
            numTiles = new Vector2Int((resolution.x - 1) / TILE_SIZE + 1, (resolution.y - 1) / TILE_SIZE + 1);
            cs.SetInts(resolutionId, new[] { resolution.x, resolution.y });
            cs.SetInts(numTilesId, new[] { numTiles.x, numTiles.y });

            // Resize buffers when resolution changes
            fragmentBuffer?.Release();
            tileFragmentCountBuffer?.Release();
            tileSortBuffer?.Release();

            fragmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, resolution.x * resolution.y, FragmentDataSize);
            tileFragmentCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numTiles.x * numTiles.y, TileFragmentCountSize);
            tileSortBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numTiles.x * numTiles.y * TILE_MAX_FRAGMENTS, SortIndexSize);
        }

        // This check is to avoid an error from the material preview in the scene
        if (resolution.x == 0 || resolution.y == 0 || !srcCamColor.IsValid() || !dst.IsValid())
            return;

        BufferHandle fragmentHandle = renderGraph.ImportBuffer(fragmentBuffer);
        BufferHandle tileFragmentCountHandle = renderGraph.ImportBuffer(tileFragmentCountBuffer);
        BufferHandle tileSortHandle = renderGraph.ImportBuffer(tileSortBuffer);

        using (var builder = renderGraph.AddComputePass("TileBufferBuild", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = tileBufferBuildIndex;
            passData.bufferList = new()
            {
                (fragmentBufferId, fragmentHandle, AccessFlags.Write),
                (tileFragmentCountBufferId, tileFragmentCountHandle, AccessFlags.Write),
                (tileSortBufferId, tileSortHandle, AccessFlags.Write),
            };
            passData.textureList = new()
            {
                (iColorId, srcCamColor, AccessFlags.Read),
                (iDepthId, srcCamDepth, AccessFlags.Read),
            };
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        using (var builder = renderGraph.AddComputePass("TileBufferSplat", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = tileBufferSplatIndex;
            passData.bufferList = new()
            {
                (fragmentBufferId, fragmentHandle, AccessFlags.Read),
                (tileFragmentCountBufferId, tileFragmentCountHandle, AccessFlags.ReadWrite),
                (tileSortBufferId, tileSortHandle, AccessFlags.Write),
            };
            passData.textureList = new();
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        using (var builder = renderGraph.AddComputePass("BlurTest", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = blurTestIndex;
            passData.bufferList = new();
            passData.textureList = new()
            {
                (iColorId, srcCamColor, AccessFlags.Read),
                (oColorId, dst, AccessFlags.Write),
                (iDepthId, srcCamDepth, AccessFlags.Read),
            };
            passData.threadGroups = new(Mathf.CeilToInt(cameraData.cameraTargetDescriptor.width / 8), Mathf.CeilToInt(cameraData.cameraTargetDescriptor.height / 8), 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        using (var builder = renderGraph.AddComputePass("SortFragments", out PassData passData))
        {
            BufferHandle indicesBufferHandle = renderGraph.ImportBuffer(indicesBuffer);
            BufferHandle depthsBufferHandle = renderGraph.ImportBuffer(depthsBuffer);

            passData.cs = cs;
            passData.kernelIndex = sortFragmentsIndex;
            passData.bufferList = new()
            {
                (Shader.PropertyToID("indices"), indicesBufferHandle, AccessFlags.ReadWrite),
                (Shader.PropertyToID("depths"), depthsBufferHandle, AccessFlags.ReadWrite)
            };
            passData.textureList = new();
            passData.threadGroups = new(1, 1, 1);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) =>
            {
                List<int> range = Enumerable.Range(0, 1024).ToList();
                List<float> randomDepths = Enumerable.Range(0, 1024).Select(_ => Random.Range(0.0f, 1.0f)).ToList();

                // Debug.Log("SortFragments Start");

                // string rangeStr;
                // string depthsStr;

                // rangeStr = "index before running compute shader: ";
                // foreach (var item in range)
                // {
                //     rangeStr += item.ToString() + " ";
                // }

                // depthsStr = "depths before running compute shader: ";
                // foreach (var item in randomDepths)
                // {
                //     depthsStr += item.ToString() + " ";
                // }
                // Debug.Log(rangeStr);
                // Debug.Log(depthsStr);

                indicesBuffer.SetData(range);
                depthsBuffer.SetData(randomDepths);

                ExecutePass(data, cgContext);
            });
        }


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
        foreach (var (id, bufferHandle, _) in data.bufferList)
            cgContext.cmd.SetComputeBufferParam(data.cs, data.kernelIndex, id, bufferHandle);
        foreach (var (id, textureHandle, _) in data.textureList)
            cgContext.cmd.SetComputeTextureParam(data.cs, data.kernelIndex, id, textureHandle);
        cgContext.cmd.DispatchCompute(data.cs, data.kernelIndex, data.threadGroups.x, data.threadGroups.y, data.threadGroups.z);
    }

    public void Dispose()
    {
        fragmentBuffer?.Release();
        tileFragmentCountBuffer?.Release();
        tileSortBuffer?.Release();

        indicesBuffer?.Release();
        depthsBuffer?.Release();
    }
}