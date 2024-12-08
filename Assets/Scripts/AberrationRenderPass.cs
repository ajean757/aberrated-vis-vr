using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;


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
    private const int TILE_MAX_FRAGMENTS = 4096;
    private const int BOX_BLUR_RADIUS = 8;

    // Struct sizes in compute shader
    private const int FragmentDataSize = 7 * sizeof(float);
    private const int TileFragmentCountSize = sizeof(uint);
    private const int SortIndexSize = sizeof(uint) + sizeof(float);

    // Params
    private static readonly int separateAberrationsId = Shader.PropertyToID("separateAberrations");
    private static readonly int numTilesId = Shader.PropertyToID("numTiles");
    private static readonly int resolutionId = Shader.PropertyToID("resolution");

    private int2 minMaxBlurRadiusCurrent = int2.zero;
    private int2 rightMinMaxBlurRadiusCurrent = int2.zero;
    private static readonly int minBlurRadiusCurrentId = Shader.PropertyToID("minBlurRadiusCurrent");
    private static readonly int maxBlurRadiusCurrentId = Shader.PropertyToID("maxBlurRadiusCurrent");
    private static readonly int rightMinBlurRadiusCurrentId = Shader.PropertyToID("rightMinBlurRadiusCurrent");
    private static readonly int rightMaxBlurRadiusCurrentId = Shader.PropertyToID("rightMaxBlurRadiusCurrent");

    private static readonly int numObjectDioptresId = Shader.PropertyToID("numObjectDioptres");
    private static readonly int objectDioptresMinId = Shader.PropertyToID("objectDioptresMin");
    private static readonly int objectDioptresMaxId = Shader.PropertyToID("objectDioptresMax");
    private static readonly int objectDioptresStepId = Shader.PropertyToID("objectDioptresStep");
    private static readonly int numAperturesId = Shader.PropertyToID("numApertures");
    private static readonly int aperturesMinId = Shader.PropertyToID("aperturesMin");
    private static readonly int aperturesMaxId = Shader.PropertyToID("aperturesMax");
    private static readonly int aperturesStepId = Shader.PropertyToID("aperturesStep");
    private static readonly int numFocusDioptresId = Shader.PropertyToID("numFocusDioptres");
    private static readonly int focusDioptresMinId = Shader.PropertyToID("focusDioptresMin");
    private static readonly int focusDioptresMaxId = Shader.PropertyToID("focusDioptresMax");
    private static readonly int focusDioptresStepId = Shader.PropertyToID("focusDioptresStep");

    private static readonly int apertureId = Shader.PropertyToID("aperture");
    private static readonly int focusDistanceId = Shader.PropertyToID("focusDistance");
    private static readonly int nearClipId = Shader.PropertyToID("nearClip");
    private static readonly int farClipId = Shader.PropertyToID("farClip");

    private static readonly int cameraPositionId = Shader.PropertyToID("cameraPosition");
    private static readonly int rightCameraPositionId = Shader.PropertyToID("rightCameraPosition");

    private static readonly int farLowerLeftId = Shader.PropertyToID("farLowerLeft");
    private static readonly int farUpperLeftId = Shader.PropertyToID("farUpperLeft");
    private static readonly int farUpperRightId = Shader.PropertyToID("farUpperRight");
    private static readonly int farLowerRightId = Shader.PropertyToID("farLowerRight");
    private static readonly int rightFarLowerLeftId = Shader.PropertyToID("rightFarLowerLeft");
    private static readonly int rightFarUpperLeftId = Shader.PropertyToID("rightFarUpperLeft");
    private static readonly int rightFarUpperRightId = Shader.PropertyToID("rightFarUpperRight");
    private static readonly int rightFarLowerRightId = Shader.PropertyToID("rightFarLowerRight");


    // Kernels (initialized in constructor)
    private readonly int tileBufferBuildIndex;
    private readonly int tileBufferSplatIndex;
    private readonly int sortFragmentsIndex;
    private readonly int blurTestIndex;
    private readonly int interpolatePsfTextureIndex;
    private readonly int interpolatePsfTextureRightIndex;
    private readonly int convolveIndex;

    private readonly int tileBufferBuildEditorIndex;
    private readonly int convolveEditorIndex;

    // Buffers (lazily initialized when resolution changes)
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

    private static readonly int iColorArrayId = Shader.PropertyToID("iColorArray");
    private static readonly int oColorArrayId = Shader.PropertyToID("oColorArray");
    private static readonly int iDepthArrayId = Shader.PropertyToID("iDepthArray");

    // Runtime cbuffer values
    private Vector2Int numTiles = Vector2Int.zero;
    private Vector2Int resolution = Vector2Int.zero;

    // Params
    private bool usingXR;
    private bool separateAberrations; // if both eyes use same aberration, we only need 1 texture

    private ComputeShader cs;

    private AberrationSettings defaultSettings;

    private static readonly int psfParamsBufferId = Shader.PropertyToID("psfParams");
    private GraphicsBuffer psfParamsBuffer;
    private GraphicsBuffer rightPsfParamsBuffer;

    private static readonly int psfWeightsBufferId = Shader.PropertyToID("psfWeights");
    private GraphicsBuffer psfWeightsBuffer;
    private GraphicsBuffer rightPsfWeightsBuffer;

    private static readonly int interpolatedPsfParamsBufferId = Shader.PropertyToID("interpolatedPsfParams");
    private static readonly int rightInterpolatedPsfParamsBufferId = Shader.PropertyToID("rightInterpolatedPsfParams");
    private GraphicsBuffer interpolatedPsfParamsBuffer;
    private GraphicsBuffer rightInterpolatedPsfParamsBuffer;

    private static readonly int psfInterpolationBufferId = Shader.PropertyToID("psfInterpolationBuffer");
    private GraphicsBuffer psfInterpolationBuffer;
    private GraphicsBuffer rightPsfInterpolationBuffer;

    // See note in Aberration.compute
    private static readonly int psfImageId = Shader.PropertyToID("psfImage");
    private static readonly int psfTextureId = Shader.PropertyToID("psfTexture");
    private static readonly int rightPsfTextureId = Shader.PropertyToID("rightPsfTexture");
    private RenderTexture psfTexture;
    private RenderTexture rightPsfTexture;
    private bool psfTextureDirty = false;

    private static readonly int psfTextureLayerExtentsId = Shader.PropertyToID("psfTextureLayerExtents");
    private static readonly int rightPsfTextureLayerExtentsId = Shader.PropertyToID("rightPsfTextureLayerExtents");
    private GraphicsBuffer psfTextureLayerExtentsBuffer;
    private GraphicsBuffer rightPsfTextureLayerExtentsBuffer;

    public float apertureDiameter;
    public float focusDistance;

    public void SetDepth(float newDepth)
    {
        focusDistance = newDepth;
    }
    public void SetAperture(float newAperture)
    {
        apertureDiameter = newAperture;
    }

    public RenderTexture GetPsfTexture()
    {
        return psfTexture;
    }


    public PSFStack psfStack;
    public PSFStack rightPsfStack;

    public AberrationRenderPass(AberrationSettings defaultSettings, ComputeShader cs)
    {
        this.defaultSettings = defaultSettings;
        this.cs = cs;

        // determine if we are using XR or not
        bool usingXR = GameObject.FindAnyObjectByType<XRManager>().UsingXR();
        Debug.Log("Using XR: " + usingXR);
        this.usingXR = usingXR;
        this.separateAberrations = this.usingXR && (defaultSettings.PSFSet != defaultSettings.RightPSFSet);

        cs.SetBool(separateAberrationsId, separateAberrations);

        // Initialize kernel IDs
        tileBufferBuildIndex = cs.FindKernel("TileBufferBuild");
        tileBufferSplatIndex = cs.FindKernel("TileBufferSplat");
        sortFragmentsIndex = cs.FindKernel("SortFragments");
        convolveIndex = cs.FindKernel("Convolve");
        blurTestIndex = cs.FindKernel("BlurTest");
        interpolatePsfTextureIndex = cs.FindKernel("InterpolatePSFTexture");
        interpolatePsfTextureRightIndex = cs.FindKernel("InterpolatePSFTextureRight");

        tileBufferBuildEditorIndex = cs.FindKernel("TileBufferBuildEditor");
        convolveEditorIndex = cs.FindKernel("ConvolveEditor");

        // read in PSFs
        psfStack = new();
        psfStack.ReadPsfStackBinary(defaultSettings.PSFSet);

        if (separateAberrations)
				{
            rightPsfStack = new();
            rightPsfStack.ReadPsfStackBinary(defaultSettings.RightPSFSet);
				}

        // run initialization code w/ dummy value to ensure all buffers are set up before any rendering happens
        SetParams(new Vector2Int(1280, 720));
    }

    void RecalculateScaledPSFs(Vector2Int newResolution)
		{
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        psfStack.ComputeScaledPSFs(Camera.main.fieldOfView, newResolution.y);
        if (separateAberrations)
				{
            rightPsfStack.ComputeScaledPSFs(Camera.main.fieldOfView, newResolution.y);
				}
        timer.Stop();
        Debug.Log("rescale PSFs: " + (float)timer.ElapsedMilliseconds / 1000.0f);
    }

    void UpdateCbuffer(ComputeGraphContext cg)
		{
        cg.cmd.SetComputeFloatParam(cs, apertureId, apertureDiameter);
        cg.cmd.SetComputeFloatParam(cs, focusDistanceId, focusDistance);

        if (usingXR)
        {
            Vector3 cameraPositionLeft = Camera.main.GetStereoViewMatrix(Camera.StereoscopicEye.Left).GetPosition();
            cg.cmd.SetComputeFloatParams(cs, cameraPositionId, cameraPositionLeft[0], cameraPositionLeft[1], cameraPositionLeft[2]);

            Vector3[] leftCorners = new Vector3[4]; // lower left, upper left, upper right, lower right
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), Camera.main.farClipPlane, Camera.MonoOrStereoscopicEye.Left, leftCorners);

            cg.cmd.SetComputeFloatParams(cs, farLowerLeftId, leftCorners[0][0], leftCorners[0][1], leftCorners[0][2]);
            cg.cmd.SetComputeFloatParams(cs, farUpperLeftId, leftCorners[1][0], leftCorners[1][1], leftCorners[1][2]);
            cg.cmd.SetComputeFloatParams(cs, farUpperRightId, leftCorners[2][0], leftCorners[2][1], leftCorners[2][2]);
            cg.cmd.SetComputeFloatParams(cs, farLowerRightId, leftCorners[3][0], leftCorners[3][1], leftCorners[3][2]);

            Vector3 cameraPositionRight = Camera.main.GetStereoViewMatrix(Camera.StereoscopicEye.Right).GetPosition();
            cg.cmd.SetComputeFloatParams(cs, rightCameraPositionId, cameraPositionRight[0], cameraPositionRight[1], cameraPositionRight[2]);

            Vector3[] rightCorners = new Vector3[4]; // lower left, upper left, upper right, lower right
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), Camera.main.farClipPlane, Camera.MonoOrStereoscopicEye.Right, rightCorners);

            cg.cmd.SetComputeFloatParams(cs, rightFarLowerLeftId, rightCorners[0][0], rightCorners[0][1], rightCorners[0][2]);
            cg.cmd.SetComputeFloatParams(cs, rightFarUpperLeftId, rightCorners[1][0], rightCorners[1][1], rightCorners[1][2]);
            cg.cmd.SetComputeFloatParams(cs, rightFarUpperRightId, rightCorners[2][0], rightCorners[2][1], rightCorners[2][2]);
            cg.cmd.SetComputeFloatParams(cs, rightFarLowerRightId, rightCorners[3][0], rightCorners[3][1], rightCorners[3][2]);
        }
        else
        {
            Vector3 cameraPosition = Camera.main.transform.position;
            cg.cmd.SetComputeFloatParams(cs, cameraPositionId, cameraPosition[0], cameraPosition[1], cameraPosition[2]);

            Vector3[] corners = new Vector3[4]; // lower left, upper left, upper right, lower right
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), Camera.main.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);

            cg.cmd.SetComputeFloatParams(cs, farLowerLeftId, corners[0][0], corners[0][1], corners[0][2]);
            cg.cmd.SetComputeFloatParams(cs, farUpperLeftId, corners[1][0], corners[1][1], corners[1][2]);
            cg.cmd.SetComputeFloatParams(cs, farUpperRightId, corners[2][0], corners[2][1], corners[2][2]);
            cg.cmd.SetComputeFloatParams(cs, farLowerRightId, corners[3][0], corners[3][1], corners[3][2]);
        }
    }

    // this is separate from resolution change because we might want to change aperture / focus without changing resolution
    void RecalculatePSFTexture(Vector2Int newResolution)
    {
        // Set aperture diameter / focus distance uniforms - default is 5 mm pupil size focused at 8 m (optical infinity) away
        // TODO: this should be user-adjustable using some nice UI, or should maybe dynamically adjust based on scene conditions
        cs.SetFloat(apertureId, apertureDiameter);
        cs.SetFloat(focusDistanceId, focusDistance);

        // Set pinhole camera parameters
        // TODO: we will need to adjust this part for stereoscopic rendering also
        // since each eye actually has different render frustum
        cs.SetFloat(nearClipId, Camera.main.nearClipPlane);
        cs.SetFloat(farClipId, Camera.main.farClipPlane);

        if (usingXR)
        {
            Vector3 cameraPositionLeft = Camera.main.GetStereoViewMatrix(Camera.StereoscopicEye.Left).GetPosition();
            cs.SetFloats(cameraPositionId, cameraPositionLeft[0], cameraPositionLeft[1], cameraPositionLeft[2]);

            Vector3[] leftCorners = new Vector3[4]; // lower left, upper left, upper right, lower right
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), Camera.main.farClipPlane, Camera.MonoOrStereoscopicEye.Left, leftCorners);

            cs.SetFloats(farLowerLeftId, leftCorners[0][0], leftCorners[0][1], leftCorners[0][2]);
            cs.SetFloats(farUpperLeftId, leftCorners[1][0], leftCorners[1][1], leftCorners[1][2]);
            cs.SetFloats(farUpperRightId, leftCorners[2][0], leftCorners[2][1], leftCorners[2][2]);
            cs.SetFloats(farLowerRightId, leftCorners[3][0], leftCorners[3][1], leftCorners[3][2]);

            Vector3 cameraPositionRight = Camera.main.GetStereoViewMatrix(Camera.StereoscopicEye.Right).GetPosition();
            cs.SetFloats(rightCameraPositionId, cameraPositionRight[0], cameraPositionRight[1], cameraPositionRight[2]);

            Vector3[] rightCorners = new Vector3[4]; // lower left, upper left, upper right, lower right
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), Camera.main.farClipPlane, Camera.MonoOrStereoscopicEye.Right, rightCorners);

            cs.SetFloats(rightFarLowerLeftId, rightCorners[0][0], rightCorners[0][1], rightCorners[0][2]);
            cs.SetFloats(rightFarUpperLeftId, rightCorners[1][0], rightCorners[1][1], rightCorners[1][2]);
            cs.SetFloats(rightFarUpperRightId, rightCorners[2][0], rightCorners[2][1], rightCorners[2][2]);
            cs.SetFloats(rightFarLowerRightId, rightCorners[3][0], rightCorners[3][1], rightCorners[3][2]);
        }
        else
				{
            Vector3 cameraPosition = Camera.main.transform.position;
            cs.SetFloats(cameraPositionId, cameraPosition[0], cameraPosition[1], cameraPosition[2]);

            Vector3[] corners = new Vector3[4]; // lower left, upper left, upper right, lower right
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), Camera.main.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);

            cs.SetFloats(farLowerLeftId, corners[0][0], corners[0][1], corners[0][2]);
            cs.SetFloats(farUpperLeftId, corners[1][0], corners[1][1], corners[1][2]);
            cs.SetFloats(farUpperRightId, corners[2][0], corners[2][1], corners[2][2]);
            cs.SetFloats(farLowerRightId, corners[3][0], corners[3][1], corners[3][2]);
        }

        // Set min / max blur radius parameters - these are dependent on resolution / vertical field of view
        int2 blurRadiusLimits = BlurRadiusLimits(psfStack, new(resolution.x, resolution.y));
        minMaxBlurRadiusCurrent = blurRadiusLimits;
        cs.SetInt(minBlurRadiusCurrentId, blurRadiusLimits[0]);
        cs.SetInt(maxBlurRadiusCurrentId, blurRadiusLimits[1]);

        if (separateAberrations)
				{
            int2 rightBlurRadiusLimits = BlurRadiusLimits(rightPsfStack, new(resolution.x, resolution.y));
            rightMinMaxBlurRadiusCurrent = rightBlurRadiusLimits;
            cs.SetInt(rightMinBlurRadiusCurrentId, rightBlurRadiusLimits[0]);
            cs.SetInt(rightMaxBlurRadiusCurrentId, rightBlurRadiusLimits[1]);
        }

        // Set aperture diameter / focus distance evaluation range uniforms
        cs.SetInt(numObjectDioptresId, psfStack.objectDioptres.Count);
        cs.SetFloat(objectDioptresMinId, psfStack.objectDioptres.Min());
        cs.SetFloat(objectDioptresMaxId, psfStack.objectDioptres.Max());
        cs.SetFloat(objectDioptresStepId, psfStack.objectDioptresStep);
        cs.SetInt(numAperturesId, psfStack.apertureDiameters.Count);
        cs.SetFloat(aperturesMinId, psfStack.apertureDiameters.Min());
        cs.SetFloat(aperturesMaxId, psfStack.apertureDiameters.Max());
        cs.SetFloat(aperturesStepId, psfStack.apertureDiametersStep);
        cs.SetInt(numFocusDioptresId, psfStack.focusDioptres.Count);
        cs.SetFloat(focusDioptresMinId, psfStack.focusDioptres.Min());
        cs.SetFloat(focusDioptresMaxId, psfStack.focusDioptres.Max());
        cs.SetFloat(focusDioptresStepId, psfStack.focusDioptresStep);

        // Free old buffers, PSF texture if necessary
        psfParamsBuffer?.Release();
        psfWeightsBuffer?.Release();
        interpolatedPsfParamsBuffer?.Release();
        psfInterpolationBuffer?.Release();
        psfTexture?.Release();
        psfTextureLayerExtentsBuffer?.Release();

        if (separateAberrations)
				{
            rightPsfParamsBuffer?.Release();
            rightPsfWeightsBuffer?.Release();
            rightInterpolatedPsfParamsBuffer?.Release();
            rightPsfInterpolationBuffer?.Release();
            rightPsfTexture?.Release();
            rightPsfTextureLayerExtentsBuffer?.Release();
				}

        // Create Buffers for PSF interpolation
        int psfParamStructSize = Marshal.SizeOf<PsfParam>();
        psfParamsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfStack.PSFCount(), psfParamStructSize);
        psfParamsBuffer.name = "PSF Parameters";

        psfWeightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfStack.TotalWeights(), sizeof(float));
        psfWeightsBuffer.name = "PSF Weights Buffer";

        int interpolatedPsfParamStructSize = Marshal.SizeOf<InterpolatedPsfParam>();
        interpolatedPsfParamsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfStack.InterpolatedPSFCount(), interpolatedPsfParamStructSize);
        interpolatedPsfParamsBuffer.name = "Interpolated PSF Parameters";

        // hardcoded to have 1024 interpolated PSFs at most
        int psfInterpolationBufferSize = 1 << 10;
        psfInterpolationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfInterpolationBufferSize, sizeof(uint));
        psfInterpolationBuffer.name = "PSF Interpolation Buffer";

        if (separateAberrations)
				{
            rightPsfParamsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rightPsfStack.PSFCount(), psfParamStructSize);
            rightPsfParamsBuffer.name = "PSF Parameters (Right)";

            rightPsfWeightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rightPsfStack.TotalWeights(), sizeof(float));
            rightPsfWeightsBuffer.name = "PSF Weights Buffer (Right)";

            rightInterpolatedPsfParamsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rightPsfStack.InterpolatedPSFCount(), interpolatedPsfParamStructSize);
            rightInterpolatedPsfParamsBuffer.name = "Interpolated PSF Parameters (Right)";

            rightPsfInterpolationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, psfInterpolationBufferSize, sizeof(uint));
            rightPsfInterpolationBuffer.name = "PSF Interpolation Buffer (Right)";
        }

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
                    if ((interpolant > values[i] && interpolant < values[i + 1]) || interpolant == values[i])
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
                    i01.focus = lowerFocus;
                    i01.aperture = upperAperture;
                    PSFIndex i10 = idx;
                    i10.focus = upperFocus;
                    i10.aperture = lowerAperture;
                    PSFIndex i11 = idx;
                    i11.focus = upperFocus;
                    i11.aperture = upperAperture;

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

                    float interpolatedBlurRadiusPx = BlurRadiusPixels(interpolatedBlurRadius, new(newResolution.x, newResolution.y), Camera.main.fieldOfView);

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

        uint[] CreatePsfInterpolationBuffer(InterpolatedPsfParam[] interpolatedPsfParams)
        {
            uint psfIndex = 0;
            uint[] psfInterpolationBuffer = new uint[psfInterpolationBufferSize];
            for (int k = 0; k < interpolatedPsfParams.Length; k += 3) // only R channel contains the startLayer / numLayer info
            {
                InterpolatedPsfParam interpolatedPsfParam = interpolatedPsfParams[k];
                for (uint i = interpolatedPsfParam.startLayer; i < (interpolatedPsfParam.startLayer + interpolatedPsfParam.numLayers); i += 1)
                {
                    psfInterpolationBuffer[i + 1] = psfIndex;
                }
                psfIndex += 1;
            }

            InterpolatedPsfParam lastInterpolatedPsfParam = interpolatedPsfParams[interpolatedPsfParams.Length - 3]; // see above note
            uint totalTextureLayers = lastInterpolatedPsfParam.startLayer + lastInterpolatedPsfParam.numLayers;
            psfInterpolationBuffer[0] = totalTextureLayers;

            return psfInterpolationBuffer;
        }

        // Move Data onto GPU at Pass Creation time

        // "C# PSF parameter buffer"
        PsfParam[] csPsfParamsBuffer = CreatePsfParamBuffer(psfStack);
        psfParamsBuffer.SetData(csPsfParamsBuffer);

        float[] csPsfWeightsBuffer = CreatePsfWeightBuffer(psfStack);
        psfWeightsBuffer.SetData(csPsfWeightsBuffer);

        // Interpolate blur radius over aperture diameter / focus distance. 
        InterpolatedPsfParam[] csInterpolatedPsfParamsBuffer = CreateInterpolatedPsfParamBuffer(psfStack, 1.0f / focusDistance, apertureDiameter);
        interpolatedPsfParamsBuffer.SetData(csInterpolatedPsfParamsBuffer);

        uint[] csPsfInterpolationBuffer = CreatePsfInterpolationBuffer(csInterpolatedPsfParamsBuffer);
        psfInterpolationBuffer.SetData(csPsfInterpolationBuffer);

        PsfParam[] csRightPsfParamsBuffer = null;
        float[] csRightPsfWeightsBuffer = null;
        InterpolatedPsfParam[] csRightInterpolatedPsfParamsBuffer = null;
        uint[] csRightPsfInterpolationBuffer = null;

        if (separateAberrations)
				{
            csRightPsfParamsBuffer = CreatePsfParamBuffer(rightPsfStack);
            rightPsfParamsBuffer.SetData(csRightPsfParamsBuffer);

            csRightPsfWeightsBuffer = CreatePsfWeightBuffer(rightPsfStack);
            rightPsfWeightsBuffer.SetData(csRightPsfWeightsBuffer);

            // Interpolate blur radius over aperture diameter / focus distance. 
            csRightInterpolatedPsfParamsBuffer = CreateInterpolatedPsfParamBuffer(rightPsfStack, 1.0f / focusDistance, apertureDiameter);
            rightInterpolatedPsfParamsBuffer.SetData(csRightInterpolatedPsfParamsBuffer);

            csRightPsfInterpolationBuffer = CreatePsfInterpolationBuffer(csRightInterpolatedPsfParamsBuffer);
            rightPsfInterpolationBuffer.SetData(csRightPsfInterpolationBuffer);
        }

        int numSlices = CalculateNumSlices(psfStack, new(resolution.x, resolution.y), Camera.main.fieldOfView);
        psfTexture = new RenderTexture(blurRadiusLimits[1] * 2 + 1, blurRadiusLimits[1] * 2 + 1, 0, RenderTextureFormat.RGB111110Float)
        {
            enableRandomWrite = true,
            dimension = TextureDimension.Tex3D,
            volumeDepth = numSlices,
            useMipMap = false,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        psfTextureLayerExtentsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numSlices, sizeof(uint));
        psfTextureLayerExtentsBuffer.name = "PSF Texture Layer Extents";

        int rightNumSlices = 0;
        if (separateAberrations)
				{
            rightNumSlices = CalculateNumSlices(rightPsfStack, new(resolution.x, resolution.y), Camera.main.fieldOfView);
            rightPsfTexture = new RenderTexture(rightMinMaxBlurRadiusCurrent[1] * 2 + 1, rightMinMaxBlurRadiusCurrent[1] * 2 + 1, 0, RenderTextureFormat.RGB111110Float)
            {
                enableRandomWrite = true,
                dimension = TextureDimension.Tex3D,
                volumeDepth = rightNumSlices,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            rightPsfTextureLayerExtentsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rightNumSlices, sizeof(uint));
            rightPsfTextureLayerExtentsBuffer.name = "PSF Texture Layer Extents (Right)";
        }


        uint[] CreatePsfTextureLayerExtentsBuffer(PSFStack stack, InterpolatedPsfParam[] interpolatedPsfParams, uint[] psfInterpolationBuffer, int numLayers)
        {
            uint[] psfTextureLayerExtents = new uint[numLayers];
            for (int layerId = 0; layerId < numLayers; layerId += 1)
            {
                uint psfId = psfInterpolationBuffer[layerId + 1];
                uint idx = psfId * 3 + 0;
                uint s = interpolatedPsfParams[idx].startLayer;
                uint n = interpolatedPsfParams[idx].numLayers;
                float psfIdx = psfId + (float)(layerId - s) / (float)(n);
                int lowerPsfIdx = Mathf.FloorToInt(psfIdx);
                int upperPsfIdx = Mathf.CeilToInt(psfIdx);
                lowerPsfIdx = Mathf.Clamp(lowerPsfIdx, 0, stack.objectDioptres.Count - 1);
                upperPsfIdx = Mathf.Clamp(upperPsfIdx, 0, stack.objectDioptres.Count - 1);

                float maxBlurRadius = 0.0f;
                for (int c = 0; c < 3; c += 1)
                {
                    float psfRadius0 = interpolatedPsfParams[lowerPsfIdx * 3 + c].blurRadius;
                    float psfRadius1 = interpolatedPsfParams[upperPsfIdx * 3 + c].blurRadius;
                    float lerped = Mathf.Lerp(psfRadius0, psfRadius1, psfIdx - (float)lowerPsfIdx);
                    maxBlurRadius = Mathf.Max(maxBlurRadius, lerped);
                }

                int textureLayerExtent = Mathf.CeilToInt(maxBlurRadius);
                // Debug.Log("layer: " + layerId + ", texture extent: " + textureLayerExtent);
                psfTextureLayerExtents[layerId] = (uint)textureLayerExtent;
            }
            return psfTextureLayerExtents;
        }

        uint[] csPsfTextureLayerExtentsBuffer = CreatePsfTextureLayerExtentsBuffer(psfStack, csInterpolatedPsfParamsBuffer, csPsfInterpolationBuffer, numSlices);
        psfTextureLayerExtentsBuffer.SetData(csPsfTextureLayerExtentsBuffer);

        if (separateAberrations)
				{
            uint[] csRightPsfTextureLayerExtentsBuffer = CreatePsfTextureLayerExtentsBuffer(rightPsfStack, csRightInterpolatedPsfParamsBuffer, csRightPsfInterpolationBuffer, rightNumSlices);
            rightPsfTextureLayerExtentsBuffer.SetData(csRightPsfTextureLayerExtentsBuffer);
        }

        if (!psfTexture.Create())
        {
            Debug.LogError("Unable to create 3D PSF texture");
        }

        if (separateAberrations && !rightPsfTexture.Create())
				{
            Debug.LogError("Unable to create 3D PSF texture (right)");
				}

        psfTextureDirty = true;
    }

    void SetParams(Vector2Int newResolution)
    {
        resolution = newResolution;
        Debug.Log("Resolution changed to " + resolution.ToString() + " in " + (usingXR ? "play mode" : "editor mode"));
        numTiles = new Vector2Int((resolution.x - 1) / TILE_SIZE + 1, (resolution.y - 1) / TILE_SIZE + 1);
        cs.SetInts(resolutionId, new[] { resolution.x, resolution.y });
        cs.SetInts(numTilesId, new[] { numTiles.x, numTiles.y });

        // Resize buffers when resolution changes
        fragmentBuffer?.Release();
        tileFragmentCountBuffer?.Release();
        tileSortBuffer?.Release();

        fragmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, resolution.x * resolution.y * (usingXR ? 2 : 1), FragmentDataSize);
        tileFragmentCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numTiles.x * numTiles.y * (usingXR ? 2 : 1), TileFragmentCountSize);
        tileSortBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numTiles.x * numTiles.y * TILE_MAX_FRAGMENTS * (usingXR ? 2 : 1), SortIndexSize);

        RecalculateScaledPSFs(newResolution);
        RecalculatePSFTexture(newResolution);
    }

    float BlurRadiusPixels(float blurRadiusDeg, int2 resolution, float fovy)
    {
        return (blurRadiusDeg / fovy) * resolution.y;
    }

    float BlurRadiusPixels(PSF psf, int2 resolution, float fovy)
    {
        return BlurRadiusPixels(psf.blurRadiusDeg, resolution, fovy);
    }

    int2 BlurRadiusLimits(PSFStack stack, int2 resolution)
    {
        float fovy = Camera.main.fieldOfView;
        int minBlurRadius = int.MaxValue;
        int maxBlurRadius = int.MinValue;
        stack.Iterate((idx, psf) =>
        {
            float blurRadius = BlurRadiusPixels(psf, resolution, fovy);
            minBlurRadius = Mathf.Min(minBlurRadius, Mathf.FloorToInt(blurRadius));
            maxBlurRadius = Mathf.Max(maxBlurRadius, Mathf.CeilToInt(blurRadius));
        });

        return new(minBlurRadius, maxBlurRadius);
    }

    int CalculateNumSlices(PSFStack stack, int2 resolution, float fovy)
    {
        // Csoba reference: slicesPerAxis
        // The only part we care about is axisId = 0, m_numSlices[0]
        // Not sure the purpose / meaning of the code
        int numSlices = 1;
        for (int i = 0; i < stack.objectDioptres.Count - 1; i += 1) // ignore the last PSF
        {
            int numLayersNeeded = 0;

            for (int j = 0; j < stack.incidentAnglesHorizontal.Count; j += 1)
            for (int k = 0; k < stack.incidentAnglesVertical.Count; k += 1)
            for (int l = 0; l < stack.lambdas.Count; l += 1)
            for (int m = 0; m < stack.apertureDiameters.Count; m += 1)
            for (int n = 0; n < stack.focusDioptres.Count; n += 1)
            {
                PSFIndex idx0 = new(i, j, k, l, m, n);
                PSFIndex idx1 = new(i + 1, j, k, l, m, n);
                PSF psf0 = stack.GetPSF(idx0);
                PSF psf1 = stack.GetPSF(idx1);
                float psfBlurRadiusPx0 = BlurRadiusPixels(psf0, resolution, fovy);
                float psfBlurRadiusPx1 = BlurRadiusPixels(psf1, resolution, fovy);
                int blurRadiusDiff = Mathf.CeilToInt(Mathf.Abs(psfBlurRadiusPx0 - psfBlurRadiusPx1));
                numLayersNeeded = Mathf.Max(1, blurRadiusDiff, numLayersNeeded);
                                
            }
            numSlices += numLayersNeeded;
        }

        return numSlices;
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
        /*
        if (tileFragmentCountBuffer != null)
        {
            uint[] countBuffer = new uint[numTiles.x * numTiles.y];
            tileFragmentCountBuffer.GetData(countBuffer);
            string str = "cnts: ";
            foreach (var item in countBuffer)
            {
                str += item.ToString() + " ";
            }
            Debug.Log(str);
        }*/

        // if (tileSortBuffer != null)
        // {
        //     PrintSortIndexBuffer(new Vector2Int(20, 20));
        // }

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        // The following line ensures that the render pass doesn't blit
        // from the back buffer.
        if (resourceData.isActiveTargetBackBuffer)
            return;

        TextureHandle srcCamColor = resourceData.activeColorTexture;
        TextureHandle srcCamDepth = resourceData.activeDepthTexture;

        var dstDesc = renderGraph.GetTextureDesc(srcCamColor);
        //Debug.Log(dstDesc.slices.ToString() + " " + dstDesc.dimension.ToString());
        dstDesc.name = "CameraColor-Aberration";
        dstDesc.clearBuffer = false;
        dstDesc.enableRandomWrite = true;
        TextureHandle dst = renderGraph.CreateTexture(dstDesc);

        // Update the blur settings in the material
        UpdateAberrationSettings();

        // Update cbuffer values + recalculate PSF texture
        if (resolution != new Vector2Int(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height))
        {
            SetParams(new Vector2Int(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height));
        }


        // This check is to avoid an error from the material preview in the scene
        if (resolution.x == 0 || resolution.y == 0 || !srcCamColor.IsValid() || !dst.IsValid())
            return;

        BufferHandle psfWeightsHandle = renderGraph.ImportBuffer(psfWeightsBuffer);
        BufferHandle psfParamsHandle = renderGraph.ImportBuffer(psfParamsBuffer);
        BufferHandle interpolatedPsfParamsHandle = renderGraph.ImportBuffer(interpolatedPsfParamsBuffer);
        BufferHandle psfInterpolationHandle = renderGraph.ImportBuffer(psfInterpolationBuffer);
        BufferHandle psfTextureLayerExtentsHandle = renderGraph.ImportBuffer(psfTextureLayerExtentsBuffer);


        BufferHandle rightPsfWeightsHandle = new();
        BufferHandle rightPsfParamsHandle = new();
        BufferHandle rightInterpolatedPsfParamsHandle = new();
        BufferHandle rightPsfInterpolationHandle = new();
        BufferHandle rightPsfTextureLayerExtentsHandle = new();
        
        if (separateAberrations)
        {
            rightPsfWeightsHandle = renderGraph.ImportBuffer(rightPsfWeightsBuffer);
            rightPsfParamsHandle = renderGraph.ImportBuffer(rightPsfParamsBuffer);
            rightInterpolatedPsfParamsHandle = renderGraph.ImportBuffer(rightInterpolatedPsfParamsBuffer);
            rightPsfInterpolationHandle = renderGraph.ImportBuffer(rightPsfInterpolationBuffer);
            rightPsfTextureLayerExtentsHandle = renderGraph.ImportBuffer(rightPsfTextureLayerExtentsBuffer);
        }

        RTHandle psfImageRTHandle = RTHandles.Alloc(psfTexture);
        TextureHandle psfImageHandle = renderGraph.ImportTexture(psfImageRTHandle);
        
        RTHandle rightPsfImageRTHandle = null;
        TextureHandle rightPsfImageHandle = new();

        if (separateAberrations)
				{
            rightPsfImageRTHandle = RTHandles.Alloc(rightPsfTexture);
            rightPsfImageHandle = renderGraph.ImportTexture(rightPsfImageRTHandle); 
        }

        using (var builder = renderGraph.AddComputePass("InterpolatePSFTexture", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = interpolatePsfTextureIndex;
            passData.bufferList = new()
            {
                (psfWeightsBufferId, psfWeightsHandle, AccessFlags.Read),
                (psfParamsBufferId, psfParamsHandle, AccessFlags.Read),
                (interpolatedPsfParamsBufferId, interpolatedPsfParamsHandle, AccessFlags.Read),
                (psfInterpolationBufferId, psfInterpolationHandle, AccessFlags.Read),
            };
            passData.textureList = new()
            {
                (psfImageId, psfImageHandle, AccessFlags.Write)
            };

            int maxBlurRadiusCurrent = minMaxBlurRadiusCurrent[1];
            int RoundedDiv(int a, int b)
            {
                return Mathf.CeilToInt((float)a / (float)b);
            }

            int xyGroups = RoundedDiv(2 * maxBlurRadiusCurrent + 1, 8);
            int numLayers = psfTexture.volumeDepth;
            passData.threadGroups = new Vector3Int(xyGroups, xyGroups, numLayers);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => {
                // update cbuffer values - camera frustum + aperture + depth
                UpdateCbuffer(cgContext);
                ExecutePass(data, cgContext);
            });
        }

        if (separateAberrations)
				{
            using (var builder = renderGraph.AddComputePass("InterpolatePSFTexture (Right)", out PassData passData))
            {
                passData.cs = cs;
                passData.kernelIndex = interpolatePsfTextureRightIndex;
                passData.bufferList = new()
                {
                    (psfWeightsBufferId, rightPsfWeightsHandle, AccessFlags.Read),
                    (psfParamsBufferId, rightPsfParamsHandle, AccessFlags.Read),
                    (interpolatedPsfParamsBufferId, rightInterpolatedPsfParamsHandle, AccessFlags.Read),
                    (psfInterpolationBufferId, rightPsfInterpolationHandle, AccessFlags.Read),
                };
                passData.textureList = new()
                {
                    (psfImageId, rightPsfImageHandle, AccessFlags.Write)
                };

                int rightMaxBlurRadiusCurrent = rightMinMaxBlurRadiusCurrent[1];
                int RoundedDiv(int a, int b)
                {
                    return Mathf.CeilToInt((float)a / (float)b);
                }

                int xyGroups = RoundedDiv(2 * rightMaxBlurRadiusCurrent + 1, 8);
                int numLayers = rightPsfTexture.volumeDepth;
                passData.threadGroups = new Vector3Int(xyGroups, xyGroups, numLayers);

                passData.Build(builder);
                builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
            }
        }

        

        BufferHandle fragmentHandle = renderGraph.ImportBuffer(fragmentBuffer);
        BufferHandle tileFragmentCountHandle = renderGraph.ImportBuffer(tileFragmentCountBuffer);
        BufferHandle tileSortHandle = renderGraph.ImportBuffer(tileSortBuffer);

        int layers = usingXR ? 2 : 1;

        using (var builder = renderGraph.AddComputePass("TileBufferBuild", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = usingXR ? tileBufferBuildIndex : tileBufferBuildEditorIndex;
            passData.bufferList = new()
            {
                (fragmentBufferId, fragmentHandle, AccessFlags.Write),
                (tileFragmentCountBufferId, tileFragmentCountHandle, AccessFlags.Write),
                (tileSortBufferId, tileSortHandle, AccessFlags.Write),

                (interpolatedPsfParamsBufferId, interpolatedPsfParamsHandle, AccessFlags.Read),
                (rightInterpolatedPsfParamsBufferId, separateAberrations ? rightInterpolatedPsfParamsHandle : interpolatedPsfParamsHandle, AccessFlags.Read)
            };
            passData.textureList = new()
            {
                (usingXR ? iColorArrayId : iColorId, srcCamColor, AccessFlags.Read),
                (usingXR ? iDepthArrayId : iDepthId, srcCamDepth, AccessFlags.Read),
            };
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, layers);

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
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, layers);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }

        using (var builder = renderGraph.AddComputePass("SortFragments", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = sortFragmentsIndex;
            passData.bufferList = new()
            {
                (tileFragmentCountBufferId, tileFragmentCountHandle, AccessFlags.Read),
                (tileSortBufferId, tileSortHandle, AccessFlags.ReadWrite),
            };
            passData.textureList = new();
            passData.threadGroups = new(layers, numTiles.x, numTiles.y);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }
        
        using (var builder = renderGraph.AddComputePass("Convolve", out PassData passData))
        {
            passData.cs = cs;
            passData.kernelIndex = usingXR ? convolveIndex : convolveEditorIndex;
            passData.bufferList = new()
            {
                (fragmentBufferId, fragmentHandle, AccessFlags.Read),
                (tileFragmentCountBufferId, tileFragmentCountHandle, AccessFlags.Read),
                (tileSortBufferId, tileSortHandle, AccessFlags.Read),

                (interpolatedPsfParamsBufferId, interpolatedPsfParamsHandle, AccessFlags.Read),
                (psfTextureLayerExtentsId, psfTextureLayerExtentsHandle, AccessFlags.Read),

                (rightInterpolatedPsfParamsBufferId, separateAberrations ? rightInterpolatedPsfParamsHandle : interpolatedPsfParamsHandle, AccessFlags.Read),
                (rightPsfTextureLayerExtentsId, separateAberrations ? rightPsfTextureLayerExtentsHandle : psfTextureLayerExtentsHandle, AccessFlags.Read)
            };
            passData.textureList = new()
            {
                (psfTextureId, psfImageHandle, AccessFlags.Read),
                (rightPsfTextureId, separateAberrations ? rightPsfImageHandle : psfImageHandle, AccessFlags.Read),
                (usingXR ? oColorArrayId : oColorId, dst, AccessFlags.Write),
            };
            passData.threadGroups = new Vector3Int(numTiles.x, numTiles.y, layers);

            passData.Build(builder);
            builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
        }
        
        // using (var builder = renderGraph.AddComputePass("BlurTest", out PassData passData))
        // {
        //    passData.cs = cs;
        //    passData.kernelIndex = blurTestIndex;
        //    passData.bufferList = new();
        //    passData.textureList = new()
        //    {
        //        //(iColorId, srcCamColor, AccessFlags.Read),
        //        (oColorId, dst, AccessFlags.Write),
        //        //(iDepthId, srcCamDepth, AccessFlags.Read),
        //    };
        //    passData.threadGroups = new(Mathf.CeilToInt(cameraData.cameraTargetDescriptor.width / 8), Mathf.CeilToInt(cameraData.cameraTargetDescriptor.height / 8), 2);
        
        //    passData.Build(builder);
        //    builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
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

        psfParamsBuffer?.Release();
        psfWeightsBuffer?.Release();
        interpolatedPsfParamsBuffer?.Release();
        psfInterpolationBuffer?.Release();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SortIndex
    {
        public uint fragmentIndex;
        public float depth;
    }

    public void PrintSortIndexBuffer(Vector2Int tileCoord)
    {
        int tileIndex = tileCoord.y * numTiles.x + tileCoord.x;

        uint[] countBuffer = new uint[1];
        tileFragmentCountBuffer.GetData(countBuffer, 0, tileIndex, 1);

        SortIndex[] sortIndexArray = new SortIndex[TILE_MAX_FRAGMENTS];
        tileSortBuffer.GetData(sortIndexArray, 0, tileIndex * TILE_MAX_FRAGMENTS, (int)countBuffer[0]);

        string depthStr = "tile at " + tileCoord.ToString() + " has " + countBuffer[0].ToString() + " elements: ";
        for (int i = 0; i < countBuffer[0]; i++)
        {
            //depthStr += "(" + sortIndexArray[i].fragmentIndex.ToString() + ", " + sortIndexArray[i].depth.ToString() + ") ";
            depthStr += sortIndexArray[i].depth.ToString() + " ";
        }
        depthStr += "done";
        Debug.Log(depthStr);
    }
}

