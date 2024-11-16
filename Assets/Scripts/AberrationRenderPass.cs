using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System;


/*
struct PsfParam
{
    uint minBlurRadius; // px
    uint maxBlurRadius; // px
    uint weightStartIndex;
    float blurRadiusDeg; // this should be in degrees of image (!! not of retina. TODO: look into this)
};
*/
[StructLayout(LayoutKind.Sequential)]
struct PsfParam
{
    public uint minBlurRadius;
    public uint maxBlurRadius;
    public uint weightStartIndex;
    public float blurRadiusDeg;

    public PsfParam(uint minBlurRadius, uint maxBlurRadius, uint weightStartIndex, float blurRadiusDeg)
    {
        this.minBlurRadius = minBlurRadius;
        this.maxBlurRadius = maxBlurRadius;
        this.weightStartIndex = weightStartIndex;
        this.blurRadiusDeg = blurRadiusDeg;
    }
}

/*
struct InterpolatedPsfParam {
		uint startLayer;
		uint numLayers;
		float blurRadius;
};
*/
[StructLayout(LayoutKind.Sequential)]
struct InterpolatedPsfParam
{
    public uint startLayer;
    public uint numLayers;
    public float blurRadius; // in screen-space pixels, possibly fractional

    public InterpolatedPsfParam(uint startLayer, uint numLayers, float blurRadius)
    {
        this.startLayer = startLayer;
        this.numLayers = numLayers;
        this.blurRadius = blurRadius;
    }
}


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
    private GraphicsBuffer interpolatedPsfParamsBuffer;

    public PSFStack psfStack;

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
        psfStack.ReadPsfStack(defaultSettings.PSFSet);


        int psfParamStructSize = Marshal.SizeOf<PsfParam>();
        psfParamsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfStack.PSFCount(), psfParamStructSize);
        psfParamsBuffer.name = "PSF Parameters";

        psfWeightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfStack.TotalWeights(), sizeof(float));
        psfWeightsBuffer.name = "PSF Weights Buffer";

        int interpolatedPsfParamStructSize = Marshal.SizeOf<InterpolatedPsfParam>();
        interpolatedPsfParamsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfStack.InterpolatedPSFCount(), interpolatedPsfParamStructSize);
        interpolatedPsfParamsBuffer.name = "Interpolated PSF Parameters";


        // move data onto GPU at Pass Creation time
        PsfParam[] CreatePsfParamBuffer(PSFStack stack)
        {
            PsfParam[] psfParams = new PsfParam[stack.PSFCount()];
            uint weightCount = 0;
            stack.Iterate((idx, psf) =>
            {
                int linearIdx = stack.LinearizeIndex(idx);
                psfParams[linearIdx] = new((uint)psf.minBlurRadius, (uint)psf.maxBlurRadius, weightCount, psf.blurRadiusDeg);
                weightCount += (uint)psf.NumWeights();
            });

            return psfParams;
        }

        float[] CreatePsfWeightBuffer(PSFStack stack)
        {
            float[] psfWeights = new float[stack.TotalWeights()];
            uint idx = 0;
            stack.Iterate((_, psf) =>
            {
                for (int r = psf.minBlurRadius; r <= psf.maxBlurRadius; r += 1)
                {
                    float[,] m = psf.weights[r - psf.minBlurRadius];
                    // row-major
                    for (int i = 0; i < m.GetLength(0); i += 1)
                    {
                        for (int j = 0; j < m.GetLength(1); j += 1)
                        {
                            psfWeights[idx] = m[i, j];
                            idx += 1;
                        }
                    }
                }
            });

            return psfWeights;
        }

        // precondition: values is sorted
        Tuple<float, int, int> FindIndices(List<float> values, float interpolant)
        {
            int lower = 0;
            int upper = 0;
            if (interpolant <= values[0])
            {
                lower = 0;
                upper = 0;
            }
            else if (interpolant >= values[values.Count - 1])
            {
                lower = values.Count - 1;
                upper = values.Count - 1;
            }
            else
            {
                // linear search
                for (int i = 0; i < values.Count - 1; i += 1)
                {
                    if (interpolant > values[i] && interpolant < values[i + 1])
                    {
                        lower = i;
                        upper = i + 1;
                        break;
                    }
                }
            }

            float frac = (lower != upper) ? (interpolant - values[lower]) / (values[upper] - values[lower]) : 0.0f;
            return new(frac, lower, upper);
        }

        // Interpolate out the focus distance and aperture diameter dimensions. 
        InterpolatedPsfParam[] CreateInterpolatedPsfParamBuffer(PSFStack stack, float focusDioptre, float apertureDiameter)
        {
            int interpolatedPsfParamCount = stack.PSFCount() / (stack.focusDioptres.Count * stack.apertureDiameters.Count);
            InterpolatedPsfParam[] interpolatedPsfParams = new InterpolatedPsfParam[interpolatedPsfParamCount];
            int i = 0;

            stack.Iterate((idx, _) =>
            {
                if (idx.aperture == 0 && idx.focus == 0)
                {
                    (float fracFocus, int lowerFocus, int upperFocus) = FindIndices(stack.focusDioptres, focusDioptre);
                    (float fracAperture, int lowerAperture, int upperAperture) = FindIndices(stack.apertureDiameters, apertureDiameter);

                    PSFIndex i00 = idx;
                    i00.focus = lowerFocus;
                    i00.aperture = lowerAperture;
                    PSFIndex i01 = idx;
                    i00.focus = lowerFocus;
                    i00.aperture = upperAperture;
                    PSFIndex i10 = idx;
                    i00.focus = upperFocus;
                    i00.aperture = lowerAperture;
                    PSFIndex i11 = idx;
                    i00.focus = upperFocus;
                    i00.aperture = upperAperture;

                    float interpolatedBlurRadius = Mathf.Lerp(
                        Mathf.Lerp(
                            stack.GetPSF(i00).blurRadiusDeg,
                            stack.GetPSF(i01).blurRadiusDeg,
                            fracAperture
                        ),
                        Mathf.Lerp(
                            stack.GetPSF(i10).blurRadiusDeg,
                            stack.GetPSF(i11).blurRadiusDeg,
                            fracAperture
                        ),
                        fracFocus
                    );

                    float interpolatedBlurRadiusPx = ProjectBlurRadius(interpolatedBlurRadius);

                    interpolatedPsfParams[i] = new(0, 0, interpolatedBlurRadiusPx);
                    i += 1;
                }
            });

            int numTextureLayers = 0;
            for (int psfIndex = 0; psfIndex < stack.objectDistances.Count; psfIndex += 1)
            {
                int maxNumLayers = 1;
                if (psfIndex < stack.objectDistances.Count - 1)
                {
                    for (int channel = 0; channel < stack.lambdas.Count; channel += 1)
                    {
                        float r0 = interpolatedPsfParams[(psfIndex) * stack.lambdas.Count + channel].blurRadius;
                        float r1 = interpolatedPsfParams[(psfIndex + 1) * stack.lambdas.Count + channel].blurRadius;
                        int textureLayers = Mathf.CeilToInt(Mathf.Abs(r1 - r0));
                        maxNumLayers = Mathf.Max(maxNumLayers, textureLayers);
                    }
                }

                interpolatedPsfParams[(psfIndex) * stack.lambdas.Count].startLayer = (uint)numTextureLayers;
                interpolatedPsfParams[(psfIndex) * stack.lambdas.Count].numLayers = (uint)maxNumLayers;
                numTextureLayers += maxNumLayers;
            }

            return interpolatedPsfParams;
        }

        // "C# PSF parameter buffer"
        PsfParam[] csPsfParamsBuffer = CreatePsfParamBuffer(psfStack);
        psfParamsBuffer.SetData(csPsfParamsBuffer);

        float[] csPsfWeightsBuffer = CreatePsfWeightBuffer(psfStack);
        psfWeightsBuffer.SetData(csPsfWeightsBuffer);

        // 1. Interpolate blur radius over aperture diameter / focus distance. For now, we only have 1 aperture / 1 focus, so the actual values of aperture / focus are irrelevant
        InterpolatedPsfParam[] csInterpolatedPsfParamsBuffer = CreateInterpolatedPsfParamBuffer(psfStack, 0.0f, 0.0f);
        interpolatedPsfParamsBuffer.SetData(csInterpolatedPsfParamsBuffer);

    }

    float ProjectBlurRadius(float blurRadius)
    {
        return (blurRadius / Camera.main.fieldOfView) * Screen.height;
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

        psfParamsBuffer?.Release();
        psfWeightsBuffer?.Release();
    }
}

