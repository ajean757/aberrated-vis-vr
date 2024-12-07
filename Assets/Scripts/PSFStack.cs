using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;


/*
 * Note: A PSF should have a single "canonical" size when evaluated at a specific (object distance, horizontal angle, vertical angle, wavelength, aperture diameter, focus distance)
 * The reason that Csoba (and by extension we) store multiple downscaled / upscaled versions is that for arbitrary object distances, we might need to sample at arbitrary blur radius
 * i.e. two closest PSFs for a object distance have "canonical" radius of 3 and 7. Depending on interpolation factor, we might need to sample blur kernels of size 4, 5, 6 from both neighboring PSFs
 * Just downscaling / upscaling the PSFs seems a little sus, but it is the approach taken in the paper so we will follow.
 */
public class PSF
{
    // jagged multidimensional array
    public float[][,] weights;

    // unscaled weights (in "retina-space")
    public float[,] rawWeights;

    // Parameters at which this PSF was evaluated
    public float objectDioptre;
    public float incidentAngleHorizontal;
    public float incidentAngleVertical;
    public float lambda;
    public float apertureDiameter;
    public float focusDioptre;

    // "derived" PSF parameters for runtime interpolation
    public int minBlurRadius;
    public int maxBlurRadius;
    public float blurRadiusDeg; // size of PSF on retina (in degrees)
    public int NumWeights()
    {
        return weights.Select(x => x.Length).Sum();
    }

    // Compute derived parameters + rescale the PSFs to match render resolution + vertical FOV
    // input parameters: idx - index of this PSF, stack - PSF stack, vfov - vertical FOV, yres - vertical resolution in pixels
    public void CreateScaledWeights(PSFIndex idx, PSFStack stack, float vfov, int yres)
    {
        // Csoba reference: blurRadiusLimitsEntry, arePsfsSameOrNeighbors
        // Essentially, we have a canonical size (from projecting blurRadiusDeg of this PSF)
        // but we might need to interpolate with "neighboring" PSF
        // i.e. those which differ by at most 1 in each index
        // so find min / max of the blur radius of all of these, then rescale the raw weights to those sizes
        // TODO: is this a reasonable way to do things?
        int minRadius = int.MaxValue;
        int maxRadius = int.MinValue;
        for (int i = -1; i <= 1; i += 1)
        {
            for (int j = -1; j <= 1; j += 1)
            {
                for (int k = -1; k <= 1; k += 1)
                {
                    PSFIndex neighborIdx = new(
                        idx.objectDepth + i,
                        idx.horizontal, // on-axis, so only 1 value
                        idx.vertical, // see above
                        idx.lambda, // never interpolate between wavelengths (channels)
                        idx.aperture + j,
                        idx.focus + k
                    );

                    if (!stack.ValidPSFIndex(neighborIdx))
                        continue;

                    PSF neighborPSF = stack.GetPSF(neighborIdx);
                    float blurRadiusPx = (neighborPSF.blurRadiusDeg / vfov) * yres;
                    minRadius = Mathf.Min(minRadius, Mathf.FloorToInt(blurRadiusPx));
                    maxRadius = Mathf.Max(maxRadius, Mathf.CeilToInt(blurRadiusPx));
                }
            }
        }

        minBlurRadius = minRadius;
        maxBlurRadius = maxRadius;
        weights = new float[maxRadius - minRadius + 1][,];
        for (int i = minRadius; i <= maxRadius; i += 1)
        {
            int j = i - minRadius;
            int diameter = 2 * i + 1;
            weights[j] = new float[diameter, diameter];
            weights[j] = ResizeArea(rawWeights, diameter);

            // renormalize
            float sum = 0.0f;
            for (int a = 0; a < weights[j].GetLength(0); a += 1)
                for (int b = 0; b < weights[j].GetLength(1); b += 1)
                    sum += weights[j][a, b];
            for (int a = 0; a < weights[j].GetLength(0); a += 1)
                for (int b = 0; b < weights[j].GetLength(1); b += 1)
                    weights[j][a, b] /= sum;
        }

        static float[,] ResizeArea(float[,] inputMatrix, int newSize)
        {
            int oldSize = inputMatrix.GetLength(0);

            if (oldSize != inputMatrix.GetLength(1))
                throw new ArgumentException("Input matrix must be square.");

            if (newSize == oldSize)
            {
                // Return a copy of the input matrix
                float[,] x = new float[newSize, newSize];
                Array.Copy(inputMatrix, x, inputMatrix.Length);
                return x;
            }

            float[,] outputMatrix = new float[newSize, newSize];
            float scale = (float)oldSize / newSize;

            if (newSize < oldSize)
            {
                // Downsampling
                for (int i = 0; i < newSize; i++)
                {
                    float y0 = i * scale;
                    float y1 = (i + 1) * scale;

                    int yStart = (int)Math.Floor(y0);
                    int yEnd = (int)Math.Ceiling(y1) - 1;
                    if (yEnd >= oldSize) yEnd = oldSize - 1;

                    for (int j = 0; j < newSize; j++)
                    {
                        float x0 = j * scale;
                        float x1 = (j + 1) * scale;

                        int xStart = (int)Math.Floor(x0);
                        int xEnd = (int)Math.Ceiling(x1) - 1;
                        if (xEnd >= oldSize) xEnd = oldSize - 1;

                        float sum = 0f;
                        float totalArea = 0f;

                        for (int y = yStart; y <= yEnd; y++)
                        {
                            for (int x = xStart; x <= xEnd; x++)
                            {
                                // Compute overlap area between input pixel and output pixel area
                                float xOverlap = Math.Min(x1, x + 1) - Math.Max(x0, x);
                                float yOverlap = Math.Min(y1, y + 1) - Math.Max(y0, y);

                                if (xOverlap > 0 && yOverlap > 0)
                                {
                                    float area = xOverlap * yOverlap;
                                    sum += inputMatrix[y, x] * area;
                                    totalArea += area;
                                }
                            }
                        }

                        outputMatrix[i, j] = sum / totalArea;
                    }
                }
            }
            else
            {
                // Upsampling
                for (int i = 0; i < newSize; i++)
                {
                    float y = (i + 0.5f) * scale - 0.5f;
                    int ySrc = (int)Math.Round(y);
                    ySrc = Math.Min(Math.Max(ySrc, 0), oldSize - 1);

                    for (int j = 0; j < newSize; j++)
                    {
                        float x = (j + 0.5f) * scale - 0.5f;
                        int xSrc = (int)Math.Round(x);
                        xSrc = Math.Min(Math.Max(xSrc, 0), oldSize - 1);

                        outputMatrix[i, j] = inputMatrix[ySrc, xSrc];
                    }
                }
            }

            return outputMatrix;
        }
    }
}

public struct PSFIndex
{
    public int objectDepth;
    public int horizontal;
    public int vertical;
    public int lambda;
    public int aperture;
    public int focus;

    public PSFIndex(int objectDepth, int horizontal, int vertical, int lambda, int aperture, int focus)
    {
        this.objectDepth = objectDepth;
        this.horizontal = horizontal;
        this.vertical = vertical;
        this.lambda = lambda;
        this.aperture = aperture;
        this.focus = focus;
    }
}

public class PSFStack
{
    /*
     * [0] Object depth;
     * [1] Horizontal axis;
     * [2] Vertical axis;
     * [3] Wavelength;
     * [4] Aperture diameter;
     * [5] Focus distance;
    */
    public PSF[,,,,,] stack;

    // Evaluated parameters
    public List<float> objectDistances;
    public List<float> objectDioptres;
    public List<float> incidentAnglesHorizontal;
    public List<float> incidentAnglesVertical;
    public List<float> lambdas;
    public List<float> apertureDiameters;
    public List<float> focusDioptres;
    public List<float> focusDistances;

    public float objectDioptresStep;
    public float apertureDiametersStep;
    public float focusDioptresStep;

    #region IO
    public void ReadPsfStackBinary(string psfSetName)
    {
        ReadPsfSamplingParameters(psfSetName);

        // Adjust filename as needed
        string psfFilename = Path.Combine(psfSetName, "psf");
        psfFilename = psfFilename.Replace(Path.DirectorySeparatorChar, '/');

        int n = PSFCount(); // Number of PSFs

				System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

        using (BinaryReader br = new BinaryReader(File.OpenRead(psfFilename)))
        {
            // Read each PSF entry
            for (int idx = 0; idx < n; idx++)
            {
                float objectDioptre = 1.0f / br.ReadSingle(); // encoded as distance in binary format
                float horizAngle = br.ReadSingle();
                float vertAngle = br.ReadSingle();
                float lambda = br.ReadSingle();
                float aperture = br.ReadSingle();
                float focus = 1.0f / br.ReadSingle(); // see above
                float blurRadiusDeg = br.ReadSingle();

                uint k = br.ReadUInt32();
                float[,] w = new float[k, k];

                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        w[j, i] = br.ReadSingle(); // data is serialized in column-major format, we expect row-major in the rest of the code
                    }
                }

                PSF psf = new PSF
                {
                    objectDioptre = objectDioptre,
                    incidentAngleHorizontal = horizAngle,
                    incidentAngleVertical = vertAngle,
                    lambda = lambda,
                    apertureDiameter = aperture,
                    focusDioptre = focus,
                    blurRadiusDeg = blurRadiusDeg,
                    rawWeights = w
                };

                var (a, b, c, d, e, f) = SplitIndex(idx);
                this.stack[a, b, c, d, e, f] = psf;
            }
        }

        timer.Stop();
        Debug.Log("read in PSFs: " + (float)timer.ElapsedMilliseconds / 1000.0f);
    }

    public void ReadPsfStackText(string psfSetName)
		{
        ReadPsfSamplingParameters(psfSetName);
        ReadPsfFiles(psfSetName);
		}

    void ReadPsfSamplingParameters(string psfSetName)
    {
        string psfStackFilename = Path.Combine(psfSetName, "psfstack");
        psfStackFilename = psfStackFilename.Replace(Path.DirectorySeparatorChar, '/');
        string psfStackFileText = File.ReadAllText(psfStackFilename);
        // very hardcoded at the moment
        string[] lines = psfStackFileText.Split(new string[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        List<float> ParseLine(string line)
        {
            string data = line.Split(':')[1];
            string[] floats = data.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            List<float> v = new();
            foreach (string s in floats)
            {
                v.Add(float.Parse(s));
            }

            return v;
        }

        this.focusDioptres = ParseLine(lines[0]);
        this.focusDistances = ParseLine(lines[1]);
        this.objectDioptres = ParseLine(lines[2]);
        this.objectDistances = ParseLine(lines[3]);
        this.lambdas = ParseLine(lines[4]);
        this.apertureDiameters = ParseLine(lines[5]);
        this.incidentAnglesHorizontal = ParseLine(lines[6]);
        this.incidentAnglesVertical = ParseLine(lines[7]);

        // if only 1 value, step size is irrelevant (as long as it's not zero)
        objectDioptresStep = objectDioptres.Count > 1 ? Mathf.Abs(objectDioptres[1] - objectDioptres[0]) : 1.0f;
        apertureDiametersStep = apertureDiameters.Count > 1 ? Mathf.Abs(apertureDiameters[1] - apertureDiameters[0]) : 1.0f;
        focusDioptresStep = focusDioptres.Count > 1 ? Mathf.Abs(focusDioptres[1] - focusDioptres[0]) : 1.0f;

        this.stack = new PSF[
          objectDistances.Count,
          incidentAnglesHorizontal.Count,
          incidentAnglesVertical.Count,
          lambdas.Count,
          apertureDiameters.Count,
          focusDioptres.Count
        ];
    }

    void ReadPsfFiles(string psfSetName)
    {
        for (int i = 0; i < objectDistances.Count; i += 1)
        {
            for (int j = 0; j < incidentAnglesHorizontal.Count; j += 1)
            {
                for (int k = 0; k < incidentAnglesVertical.Count; k += 1)
                {
                    for (int l = 0; l < lambdas.Count; l += 1)
                    {
                        for (int m = 0; m < apertureDiameters.Count; m += 1)
                        {
                            for (int n = 0; n < focusDioptres.Count; n += 1)
                            {
                                stack[i, j, k, l, m, n] = new();
                                ReadPsfFile(psfSetName, new int[] { i, j, k, l, m, n });
                            }
                        }
                    }
                }
            }
        }
    }

    void ReadPsfFile(string psfSetName, int[] indices)
    {
        // format:
        // [0] Object depth;
        // [1] Horizontal axis;
        // [2] Vertical axis;
        // [3] Wavelength;
        // [4] Aperture diameter;
        // [5] Focus distance;
        PSF psf = GetPSF(indices);
        string filename = $"{indices[0]}-{indices[1]}-{indices[2]}-{indices[3]}-{indices[4]}-{indices[5]}.psf";
        string path = Path.Combine(psfSetName, filename);
        path = path.Replace(Path.DirectorySeparatorChar, '/');
        string psfFileText = File.ReadAllText(path);

        MatchCollection matches = Regex.Matches(psfFileText, @"Radius: (\d+)\r?\n");
        psf.weights = new float[matches.Count][,];

        psf.objectDioptre = objectDioptres[indices[0]];
        psf.incidentAngleHorizontal = incidentAnglesHorizontal[indices[1]];
        psf.incidentAngleVertical = incidentAnglesVertical[indices[2]];
        psf.lambda = lambdas[indices[3]];
        psf.apertureDiameter = apertureDiameters[indices[4]];
        psf.focusDioptre = focusDioptres[indices[5]];
        psf.blurRadiusDeg = float.Parse(Regex.Match(psfFileText, @"Blur Radius: (.*) \(degs\)").Groups[1].Value);

        // Read in raw weights
        Match psfSizeMatch = Regex.Match(psfFileText, @"Unscaled PSF size: (\d+)");
        int n = int.Parse(psfSizeMatch.Groups[1].Value);
        int start = psfSizeMatch.Index + psfSizeMatch.Length;
        int end = psfFileText.Length;
        psf.rawWeights = new float[n, n];

        string array = psfFileText.Substring(start, end - start);
        string[] rows = array.Split(new string[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        int i = 0;
        foreach (string row in rows)
        {
            int j = 0;
            string[] cols = row.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string value in cols)
            {
                psf.rawWeights[i, j] = float.Parse(value);
                j += 1;
            }
            i += 1;
        }
    }

    #endregion

    #region Accessors / Utility functions
    public PSF GetPSF(int[] indices)
    {
        return stack[indices[0], indices[1], indices[2], indices[3], indices[4], indices[5]];
    }

    public PSF GetPSF(PSFIndex index)
    {
        return stack[index.objectDepth, index.horizontal, index.vertical, index.lambda, index.aperture, index.focus];
    }

    public int PSFCount()
    {
        return stack.Length;
    }

    public int InterpolatedPSFCount()
    {
        return PSFCount() / (focusDioptres.Count * apertureDiameters.Count);
    }

    public bool ValidPSFIndex(PSFIndex index)
        {
        return (index.objectDepth >= 0 && index.objectDepth < objectDistances.Count) &&
            (index.horizontal >= 0 && index.horizontal < incidentAnglesHorizontal.Count) &&
            (index.vertical >= 0 && index.vertical < incidentAnglesVertical.Count) &&
            (index.lambda >= 0 && index.lambda < lambdas.Count) &&
            (index.aperture >= 0 && index.aperture < apertureDiameters.Count) &&
            (index.focus >= 0 && index.focus < focusDioptres.Count);
        }

    public int TotalWeights()
    {
        int totalWeights = 0;
        Action<PSFIndex, PSF> psfWeights = (_, p) =>
        {
            for (int radius = p.minBlurRadius; radius <= p.maxBlurRadius; radius += 1)
            {
                int nn = 2 * radius + 1;
                totalWeights += nn * nn;
            }
        };

        Iterate(psfWeights);
        return totalWeights;
    }

    // "row-major" (i.e. lower indices have largest strides)
    public int LinearizeIndex(PSFIndex idx)
    {
        return idx.objectDepth * (incidentAnglesHorizontal.Count * incidentAnglesVertical.Count * lambdas.Count * apertureDiameters.Count * focusDioptres.Count)
            + idx.horizontal * (incidentAnglesVertical.Count * lambdas.Count * apertureDiameters.Count * focusDioptres.Count)
            + idx.vertical * (lambdas.Count * apertureDiameters.Count * focusDioptres.Count)
            + idx.lambda * (apertureDiameters.Count * focusDioptres.Count)
            + idx.aperture * (focusDioptres.Count)
            + idx.focus;
    }

    public (int, int, int, int, int, int) SplitIndex(int linearIdx) {
        int i = linearIdx / (incidentAnglesHorizontal.Count * incidentAnglesVertical.Count * lambdas.Count * apertureDiameters.Count * focusDioptres.Count);
        int j = linearIdx % (incidentAnglesHorizontal.Count * incidentAnglesVertical.Count * lambdas.Count * apertureDiameters.Count * focusDioptres.Count);

        int k = j / (incidentAnglesVertical.Count * lambdas.Count * apertureDiameters.Count * focusDioptres.Count);
        int l = j % (incidentAnglesVertical.Count * lambdas.Count * apertureDiameters.Count * focusDioptres.Count);

        int m = l / (lambdas.Count * apertureDiameters.Count * focusDioptres.Count);
        int n = l % (lambdas.Count * apertureDiameters.Count * focusDioptres.Count);

        int o = n / (apertureDiameters.Count * focusDioptres.Count);
        int p = n % (apertureDiameters.Count * focusDioptres.Count);

        int q = p / (focusDioptres.Count);
        int r = p % (focusDioptres.Count);

        int s = r / 1;
        int t = r % 1;

        return (i, k, m, o, q, s);
    }

    public PSFIndex IndexFromLinearIndex(int linearIdx)
		{
        var (i, j, k, l, m, n) = SplitIndex(linearIdx);
        return new(i, j, k, l, m, n);
		}
    public void Iterate(Action<PSFIndex, PSF> fn)
    {
        for (int i = 0; i < objectDistances.Count; i += 1)
        {
            for (int j = 0; j < incidentAnglesHorizontal.Count; j += 1)
            {
                for (int k = 0; k < incidentAnglesVertical.Count; k += 1)
                {
                    for (int l = 0; l < lambdas.Count; l += 1)
                    {
                        for (int m = 0; m < apertureDiameters.Count; m += 1)
                        {
                            for (int n = 0; n < focusDioptres.Count; n += 1)
                            {
                                PSF psf = stack[i, j, k, l, m, n];
                                PSFIndex index = new(i, j, k, l, m, n);
                                fn(index, psf);
                            }
                        }
                    }
                }
            }
        }
    }
    #endregion
    
    // see CreateScaledWeights
    public void ComputeScaledPSFs(float vfov, int yres)
    {
        // Iterate((idx, psf) =>
        // {
        //     psf.CreateScaledWeights(idx, this, vfov, yres);
        // });

        Parallel.For(0, PSFCount(), psfId =>
        {
            PSFIndex psfIndex = IndexFromLinearIndex(psfId);
            PSF psf = GetPSF(psfIndex);
            psf.CreateScaledWeights(psfIndex, this, vfov, yres);
        });
    }
}


