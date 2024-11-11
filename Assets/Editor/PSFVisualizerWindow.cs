using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PSFVisualizerWindow : EditorWindow
{
    private UniversalRenderPipelineAsset currentPipelineAsset;
    private PSFStack psfStack;
    private PSF psf;
    private float[,] matrixData;
    private int rows = 0;
    private int cols = 0;

    [MenuItem("Window/PSF Visualizer")]
    public static void ShowWindow()
    {
        PSFVisualizerWindow window = GetWindow<PSFVisualizerWindow>("PSF Visualizer");
        window.Show();
    }

    private void OnEnable()
    {
        // Access the currently active URP pipeline asset
        currentPipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

        if (currentPipelineAsset != null)
        {
            // Optionally, you can extract any data from the pipeline asset here if needed
            Debug.Log("Active URP Pipeline Asset: " + currentPipelineAsset.name);
        }
        else
        {
            Debug.LogWarning("No active URP Pipeline Asset found.");
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("PSF Visualization", EditorStyles.boldLabel);

        // Check if we found the active URP pipeline asset
        if (currentPipelineAsset != null)
        {
            // Optionally extract a field from the pipeline asset (like a matrix data field)
            if (psf == null)
            {
                psf = ExtractMatrixDataFromPipelineAsset(currentPipelineAsset);
            }

            if (psf != null)
            {
                if (matrixData == null)
                {
                    ExtractMatrixData();
                }

                ShowMatrix();
            }
            else
            {
                GUILayout.Label("No matrix data found in the pipeline asset.");
            }
        }
        else
        {
            GUILayout.Label("No active URP pipeline asset found.");
        }
    }

    private PSF ExtractMatrixDataFromPipelineAsset(UniversalRenderPipelineAsset pipelineAsset)
    {
        // Here, you can access and extract a specific field from the pipeline asset
        // For example, this might be a custom ScriptableObject field in the pipeline asset
        // In this case, we're pretending there is a MatrixDataAsset field in the URP pipeline

        // You could add a custom field like this in your custom URP pipeline asset
        // and then extract it like this:
        // return pipelineAsset.customMatrixData; // Just an example

        // If you don't have such a field in your pipeline asset, you could create one

        // unclear why there would be more than 1?
        var selectedRenderer = pipelineAsset.scriptableRenderer;

        for (var i = 0; i < pipelineAsset.renderers.Length; ++i)
        {
            if (pipelineAsset.renderers[i] == selectedRenderer)
            {
                ScriptableRendererData rendererData = pipelineAsset.rendererDataList[i];
                AberrationRendererFeature feature = null;
                if (!rendererData.TryGetRendererFeature<AberrationRendererFeature>(out feature))
                {
                    Debug.LogError("No AberrationRendererFeature found");
                }

                AberrationRenderPass renderPass = feature.GetRenderPass();
                psfStack = renderPass.psfStack;

                return psfStack.stack[0, 0, 0, 0, 0, 0];
            }
        }

        return null;
    }

    private void ExtractMatrixData()
    {
        // If you've successfully extracted the data, it would be available here
        // If the data is in a field within the MatrixDataAsset, we would load it into `matrixData`

        // Example: Assume you extracted a 2D matrix from the pipeline asset
        if (psf != null)
        {
            matrixData = psf.weights;
            rows = matrixData.GetLength(0);
            cols = matrixData.GetLength(1);
        }
    }

    private void ShowMatrix()
    {
        // Display the matrix as a grid of labels
        GUILayout.BeginHorizontal();

        for (int i = 0; i < rows; i++)
        {
            GUILayout.BeginVertical();

            for (int j = 0; j < cols; j++)
            {
                string label = matrixData[i, j].ToString("F2");
                GUILayout.Label(label, GUILayout.Width(50), GUILayout.Height(25));
            }

            GUILayout.EndVertical();
        }

        GUILayout.EndHorizontal();
    }
}