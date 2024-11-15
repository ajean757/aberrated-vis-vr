using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    // Parameters at which this PSF was evaluated
    public float objectDioptre;
    public float incidentAngleHorizontal;
    public float incidentAngleVertical;
    public float lambda;
    public float apertureDiameter;
    public float focusDioptre;

    // PSF parameters for runtime interpolation
    public int minBlurRadius;
    public int maxBlurRadius;
    public float blurRadiusDeg; // size of PSF on retina (in degrees)

    public int NumWeights()
		{
        return weights.Select(x => x.Length).Sum();
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
    List<float> objectDistances;
    List<float> objectDioptres;
    List<float> incidentAnglesHorizontal;
    List<float> incidentAnglesVertical;
    List<float> lambdas;
    List<float> apertureDiameters;
    List<float> focusDioptres;
    List<float> focusDistances;

    public void ReadPsfStack(string psfSetName)
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

        ReadPsfFiles(psfSetName);
    }

    void ReadPsfFiles(string psfSetName)
    {
        this.stack = new PSF[
          objectDistances.Count,
          incidentAnglesHorizontal.Count,
          incidentAnglesVertical.Count,
          lambdas.Count,
          apertureDiameters.Count,
          focusDioptres.Count
        ];
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

    PSF GetPSF(int[] indices)
    {
        return stack[indices[0], indices[1], indices[2], indices[3], indices[4], indices[5]];
    }

    public int PSFCount()
		{
        return stack.Length;
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

        psf.minBlurRadius = matches.Cast<Match>().Select(x => int.Parse(x.Groups[1].Value)).Min();
        psf.maxBlurRadius = matches.Cast<Match>().Select(x => int.Parse(x.Groups[1].Value)).Max();
        psf.blurRadiusDeg = float.Parse(Regex.Match(psfFileText, @"Blur Radius: (.*) \(degs\)").Groups[1].Value);

        int index = 0;
        foreach (Match match in matches)
        {
            int start = match.Index + match.Length;
            Match next = match.NextMatch();
            int end = next.Success ? next.Index : psfFileText.Length;

            int radius = int.Parse(match.Groups[1].Value);
            int n = 2 * radius + 1;
            
            psf.weights[index] = new float[n, n];

            string array = psfFileText.Substring(start, end - start);
            string[] rows = array.Split(new string[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);


            int i = 0;
            foreach (string row in rows)
            {
                int j = 0;
                string[] cols = row.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                foreach (string value in cols)
                {
                    psf.weights[index][i, j] = float.Parse(value);
                    j += 1;
                }
                i += 1;
            }

            index += 1;
        }
    }
}


