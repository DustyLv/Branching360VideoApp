using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

// This script MUST be placed in a folder named "Editor" inside your Assets folder.

public class VideoNarrativeEditor : EditorWindow
{
    // C# classes mirroring the JSON structure.
    // These are duplicated from the runtime script for editor-side use.
    [System.Serializable]
    public class Choice
    {
        public string buttonTitle;
        public string nextVideoNodeId;
    }

    [System.Serializable]
    public class VideoNode
    {
        public string id;
        public string videoFileName;
        public List<Choice> choices;
        
        // --- Editor-only data ---
        [System.NonSerialized] public Rect rect; // Position and size of the node in the editor window.
    }

    [System.Serializable]
    public class VideoManifest
    {
        public List<VideoNode> nodes;
    }

    // --- Private Fields ---
    private List<VideoNode> nodes;
    private Dictionary<string, VideoNode> nodeLookup;
    private string manifestFileName = "story.json";
    private Vector2 nodeSize = new Vector2(200, 100);
    private VideoNode selectedNode;
    private Vector2 dragOffset;

    // --- Initialization ---
    [MenuItem("Window/Video Narrative Editor")]
    public static void ShowWindow()
    {
        GetWindow<VideoNarrativeEditor>("Video Narrative Editor");
    }

    private void OnEnable()
    {
        LoadManifest();
    }

    // --- GUI Drawing ---
    private void OnGUI()
    {
        // --- Draw UI Buttons ---
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Load", EditorStyles.toolbarButton))
        {
            LoadManifest();
        }
        if (GUILayout.Button("Save", EditorStyles.toolbarButton))
        {
            SaveManifest();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();


        if (nodes == null) return;
        
        // --- Draw elements ---
        DrawConnections();
        DrawNodes();
        
        // --- Process Events ---
        ProcessNodeEvents(Event.current);
        
        // Redraw continuously to make it feel responsive
        if(GUI.changed) Repaint();
    }

    private void DrawNodes()
    {
        BeginWindows();
        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].rect = GUI.Window(i, nodes[i].rect, DrawNodeWindow, nodes[i].id);
        }
        EndWindows();
    }

    void DrawNodeWindow(int id)
    {
        VideoNode node = nodes[id];
        
        // Simple display of the video file name
        EditorGUILayout.LabelField("Video:", node.videoFileName);

        // Add more fields here to edit node properties
        
        GUI.DragWindow();
    }

    private void DrawConnections()
    {
        if (nodes == null) return;
        
        foreach (var node in nodes)
        {
            if (node.choices == null) continue;

            foreach (var choice in node.choices)
            {
                if (nodeLookup.TryGetValue(choice.nextVideoNodeId, out VideoNode targetNode))
                {
                    DrawNodeCurve(node.rect, targetNode.rect);
                }
            }
        }
    }

    void DrawNodeCurve(Rect start, Rect end)
    {
        Vector3 startPos = new Vector3(start.x + start.width, start.y + start.height / 2, 0);
        Vector3 endPos = new Vector3(end.x, end.y + end.height / 2, 0);
        Vector3 startTan = startPos + Vector3.right * 50;
        Vector3 endTan = endPos + Vector3.left * 50;
        
        Handles.DrawBezier(startPos, endPos, startTan, endTan, Color.white, null, 2f);
    }
    
    // --- Event Handling ---
    private void ProcessNodeEvents(Event e)
    {
        // This is a simplified event handler. A full implementation would be more complex.
        // For example, handling right-click context menus to add choices.
    }

    // --- File Operations ---
    private void LoadManifest()
    {
        string path = Path.Combine(Application.streamingAssetsPath, manifestFileName);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            VideoManifest manifest = JsonUtility.FromJson<VideoManifest>(json);
            nodes = manifest.nodes;
            nodeLookup = nodes.ToDictionary(n => n.id);

            // Auto-layout nodes in a simple grid for initial viewing
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].rect = new Rect(10 + (i % 4) * (nodeSize.x + 20), 50 + (i / 4) * (nodeSize.y + 20), nodeSize.x, nodeSize.y);
            }

            Debug.Log("Video manifest loaded.");
        }
        else
        {
            Debug.LogError($"Manifest not found at {path}");
            nodes = new List<VideoNode>();
            nodeLookup = new Dictionary<string, VideoNode>();
        }
        Repaint();
    }

    private void SaveManifest()
    {
        string path = Path.Combine(Application.streamingAssetsPath, manifestFileName);
        VideoManifest manifest = new VideoManifest { nodes = this.nodes };
        string json = JsonUtility.ToJson(manifest, true); // 'true' for pretty print
        File.WriteAllText(path, json);
        
        EditorUtility.DisplayDialog("Save Manifest", "Successfully saved the video manifest!", "OK");
        Debug.Log($"Video manifest saved to {path}");
    }
}
