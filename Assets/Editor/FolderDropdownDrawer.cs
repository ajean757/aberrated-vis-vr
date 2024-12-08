using UnityEditor;
using UnityEngine;
using System.IO;
using System.Reflection;

[CustomPropertyDrawer(typeof(string))]
public class FolderDropdownDrawer : PropertyDrawer
{
    private string[] folderNames = new string[0];

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // Check if the field has the FolderDropdownAttribute
        var attribute = fieldInfo.GetCustomAttribute(typeof(FolderDropdownAttribute), false);
        if (attribute == null)
        {
            // If the attribute is not present, just display the field normally
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        // Populate the folder names if not already done
        if (folderNames.Length == 0)
        {
            PopulateFolderNames();
        }

        // Display the dropdown if there are any folders
        if (folderNames.Length > 0)
        {
            // Find the index of the currently selected folder
            int currentIndex = Mathf.Max(0, System.Array.IndexOf(folderNames, property.stringValue));

            // Display the dropdown with folder names
            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, folderNames);

            property.stringValue = folderNames[newIndex];
            property.serializedObject.ApplyModifiedProperties();  // Ensure changes are applied
        }
        else
        {
            // Display a message if no folders are found
            EditorGUI.LabelField(position, label.text, "No folders found to load aberrations from.");
        }
    }

    private void PopulateFolderNames()
    {
        string resourcesPath = "Assets/Aberrations";

        // Check if the Resources folder exists
        if (Directory.Exists(resourcesPath))
        {
            // Find all subdirectories
            string[] subDirectories = Directory.GetDirectories(resourcesPath);

            for (int i = 0; i < subDirectories.Length; i += 1)
            {
                // normalize to use forward slash as directory separator
                subDirectories[i] = subDirectories[i].Replace(Path.DirectorySeparatorChar, '/');
                //Debug.Log(subDirectories[i]);
            }
            
            folderNames = subDirectories;
        }
    }
}