using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

class PSF
{
  public float[,] weights;
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
  PSF[,,,,,] stack;

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
    TextAsset psfStackFileText = Resources.Load<TextAsset>(psfStackFilename);
    // very hardcoded at the moment
    string[] lines = psfStackFileText.text.Split('\n');

    List<float> ParseLine(string line)
    {
      string data = line.Split(':')[1];
      string[] floats = data.Split(' ');
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

  void ReadPsfFiles(string psfSetName) {
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
                ReadPsfFile(psfSetName, new int[]{ i, j, k, l, m, n });
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
    TextAsset psfFileText = Resources.Load<TextAsset>(path);

    MatchCollection matches = Regex.Matches(psfFileText.text, @"Radius: (\d+)\n");
    foreach (Match match in matches)
    {
      int start = match.Index + match.Length;
      Match next = match.NextMatch();
      int end = next.Success ? next.Index : psfFileText.text.Length;

      int n = int.Parse(match.Groups[1].Value);
      psf.weights = new float[n, n];
      
      string array = psfFileText.text.Substring(start, end);
      string[] rows = array.Split('\n');


      int i = 0;
      foreach (string row in rows)
      {
        int j = 0;
        string[] cols = row.Split(' ');
        foreach (string value in cols) {
          psf.weights[i, j] = float.Parse(value);
          j += 1;
        }
        i += 1;
      }
    }
  }
}


