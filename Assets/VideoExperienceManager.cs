using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
#if PLATFORM_ANDROID || UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Manages the branching video narrative.
/// Includes Main Menu for selecting different story files.
/// Robust permission handling for Android/Quest.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VideoExperienceManager : MonoBehaviour
{
    // --- UNITY EDITOR ASSIGNMENTS ---
    
    [Header("Main Menu UI")]
    [Tooltip("The panel containing the main menu. Will be hidden when a video starts.")]
    public GameObject mainMenuPanel;
    [Tooltip("The container where file list buttons will be spawned.")]
    public Transform fileListContainer;
    [Tooltip("Prefab for the file selection button.")]
    public Button fileButtonPrefab;

    [Header("Video UI")]
    [Tooltip("The panel containing the video controls/choices. Hidden in menu.")]
    public GameObject videoUIPanel;
    public Button choiceButtonPrefab;
    public Button backButtonPrefab;
    public Transform buttonContainer;

    // --- PRIVATE FIELDS ---
    private VideoPlayer videoPlayer;
    private Dictionary<string, VideoNode> videoNodes;
    private Dictionary<string, string> parentMap = new Dictionary<string, string>();
    private string currentNodeId;
    private string startingNodeId = ""; // Now determined dynamically
    private string dataFolderPath = "";
    private string currentManifestFileName = "";
    private bool isLoadingManifest = false;

    #region Data Structures
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
        public bool isStartingNode; // New flag
        public List<Choice> choices;
    }

    [System.Serializable]
    public class VideoInfo
    {
        public string name;
        public string path;
    }

    [System.Serializable]
    public class VideoManifest
    {
        public List<VideoNode> nodes;
        public List<VideoInfo> videos;
    }
    #endregion

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.errorReceived += HandleVideoError;

        SetDataFolderPath();
    }

    void Start()
    {
        // Start the permission flow. If granted, it will open the Main Menu.
        StartCoroutine(InitializePermissionAndMenu());
    }

    private void SetDataFolderPath()
    {
        string root = GetAndroidExternalStoragePath();
        // Matches the Electron app deployment path:
        // Android: /storage/emulated/0/Android/data/<package>/files/StoryFiles
        // dataFolderPath = Path.Combine(Application.persistentDataPath, "StoryFiles");
        dataFolderPath = Path.Combine(root, "StoryFiles");
        

        if (!Directory.Exists(dataFolderPath))
        {
            try { Directory.CreateDirectory(dataFolderPath); }
            catch { /* Ignore, will retry after permission grant */ }
        }
        
        Debug.Log($"[DataPath] {dataFolderPath}");
    }
    
    public string GetAndroidExternalStoragePath()
    {
        // Check if we are actually running on Android
        if (Application.platform != RuntimePlatform.Android)
        {
            // Fallback for Editor logic
            return Application.persistentDataPath; 
        }

        try
        {
            // We need to access the java class "android.os.Environment"
            using (AndroidJavaClass environment = new AndroidJavaClass("android.os.Environment"))
            {
                // Call the static method "getExternalStorageDirectory"
                using (AndroidJavaObject file = environment.CallStatic<AndroidJavaObject>("getExternalStorageDirectory"))
                {
                    // Call the method "getAbsolutePath" on the file object
                    return file.Call<string>("getAbsolutePath");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to get Android storage path: " + e.Message);
            return null;
        }
    }
    
    public IEnumerator RequestStoragePermissionRoutine()
    {
        if (Application.platform != RuntimePlatform.Android) yield break;

        // --- Android 11+ (API 30+) Logic ---
        if (GetSDKInt() >= 30)
        {
            if (!IsExternalStorageManager())
            {
                Debug.Log("Android 11 detected: Requesting All Files Access...");
                
                // Open the specific system settings menu
                try
                {
                    using (var unityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var currentActivity = unityClass.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                    using (var settingsClass = new AndroidJavaClass("android.provider.Settings"))
                    {
                        string packageName = Application.identifier;
                        string action = settingsClass.GetStatic<string>("ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION");
                        
                        using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName))
                        using (var intent = new AndroidJavaObject("android.content.Intent", action, uri))
                        {
                            currentActivity.Call("startActivity", intent);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error opening settings: {e.Message}");
                }

                // WAIT: Wait for the user to go to settings and come back
                // First, wait for the app to lose focus (user goes to settings)
                yield return new WaitForSeconds(0.5f); 
                
                // Then wait until the app is focused again (user comes back)
                yield return new WaitUntil(() => Application.isFocused);

                // Re-check permission
                if (IsExternalStorageManager())
                {
                    Debug.Log("User granted All Files Access!");
                }
                else
                {
                    Debug.LogError("User returned but declined permission.");
                    // Optional: Show a UI popup here saying "We strictly need this permission!"
                }
            }
        }
        // --- Android 10 and below Logic ---
        else
        {
            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                Permission.RequestUserPermission(Permission.ExternalStorageWrite);

                // Wait until the dialog is closed or permission is granted
                // Note: On older Android, checking this in a tight loop works, 
                // but checking focus is often safer to prevent freezing.
                yield return new WaitForSeconds(0.2f);
                yield return new WaitUntil(() => Application.isFocused);
                
                if (Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
                {
                    Debug.Log("Standard Storage Permission Granted.");
                }
            }
        }
        
        
    }
    
    private int GetSDKInt()
    {
        using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            return buildVersion.GetStatic<int>("SDK_INT");
        }
    }

    // Helper to check the "All Files Access" boolean
    private bool IsExternalStorageManager()
    {
        using (var environment = new AndroidJavaClass("android.os.Environment"))
        {
            return environment.CallStatic<bool>("isExternalStorageManager");
        }
    }

    private IEnumerator InitializePermissionAndMenu()
    {
#if PLATFORM_ANDROID || UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
        {
            bool callbackDone = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += (perm) => { callbackDone = true; };
            callbacks.PermissionDenied += (perm) => { callbackDone = true; };
            callbacks.PermissionDeniedAndDontAskAgain += (perm) => { callbackDone = true; };

            Permission.RequestUserPermission(Permission.ExternalStorageRead, callbacks);

            // Wait for user response
            while (!callbackDone) yield return null;
        }

        yield return StartCoroutine(RequestStoragePermissionRoutine());
#endif
        // Give the OS a moment to update file system access
        yield return new WaitForSeconds(0.5f);
        
        ShowMainMenu();
    }

    // --- MAIN MENU LOGIC ---

    public void ShowMainMenu()
    {
        // Reset video state
        videoPlayer.Stop();
        videoNodes = null;
        parentMap.Clear();
        startingNodeId = ""; // Reset starting node

        // Toggle UI
        if (videoUIPanel) videoUIPanel.SetActive(false);
        if (mainMenuPanel) mainMenuPanel.SetActive(true);

        RefreshFileList();
    }

    public void RefreshFileList()
    {
        // Clear old buttons
        foreach (Transform child in fileListContainer) Destroy(child.gameObject);

        if (!Directory.Exists(dataFolderPath))
        {
            Debug.LogError($"[Menu] Data folder not found: {dataFolderPath}");
            return;
        }
        string[] files = Directory.GetFiles(dataFolderPath, "*.json");

        if (files.Length == 0)
        {
            Debug.Log("[Menu] No JSON files found.");
            return;
        }

        foreach (string filePath in files)
        {
            string fileName = Path.GetFileName(filePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            Button btn = Instantiate(fileButtonPrefab, fileListContainer);
            TextMeshProUGUI txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt) txt.text = fileNameWithoutExtension;

            btn.onClick.AddListener(() => LoadSelectedManifest(fileName));
        }
    }

    public void LoadSelectedManifest(string fileName)
    {
        currentManifestFileName = fileName;
        
        // Toggle UI
        if (mainMenuPanel) mainMenuPanel.SetActive(false);
        if (videoUIPanel) videoUIPanel.SetActive(true);

        StartCoroutine(LoadManifestCoroutine());
    }

    public void GoToMainMenu()
    {
        ShowMainMenu();
    }

    // --- MANIFEST LOADING ---

    private IEnumerator LoadManifestCoroutine()
    {
        if (isLoadingManifest) yield break;
        isLoadingManifest = true;

        string manifestPath = Path.Combine(dataFolderPath, currentManifestFileName);
        Debug.Log($"[Manifest] Loading: {manifestPath}");

        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[Manifest] Missing file: {manifestPath}");
            isLoadingManifest = false;
            ShowMainMenu(); // Go back if fail
            yield break;
        }

        string jsonContent = null;
        try
        {
            jsonContent = File.ReadAllText(manifestPath);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Manifest] Read Error: {ex.Message}");
            isLoadingManifest = false;
            ShowMainMenu();
            yield break;
        }

        if (!string.IsNullOrEmpty(jsonContent))
        {
            ParseManifest(jsonContent);
            
            if (!string.IsNullOrEmpty(startingNodeId) && videoNodes != null && videoNodes.ContainsKey(startingNodeId))
            {
                LoadStartingNode();
            }
            else
            {
                Debug.LogError($"[Manifest] No valid starting node found in {currentManifestFileName}. Check 'isStartingNode' flag.");
            }
        }

        isLoadingManifest = false;
        yield return null;
    }

    private void ParseManifest(string jsonContent)
    {
        try
        {
            VideoManifest manifest = JsonUtility.FromJson<VideoManifest>(jsonContent);
            if (manifest == null || manifest.nodes == null)
            {
                videoNodes = new Dictionary<string, VideoNode>();
                return;
            }

            videoNodes = manifest.nodes.ToDictionary(node => node.id);

            parentMap.Clear();
            startingNodeId = ""; // Reset

            foreach (var node in manifest.nodes)
            {
                // Check for starting node flag
                if (node.isStartingNode)
                {
                    startingNodeId = node.id;
                }

                if (node.choices == null) continue;
                
                foreach (var choice in node.choices)
                {
                    if (!string.IsNullOrEmpty(choice.nextVideoNodeId) && videoNodes.ContainsKey(choice.nextVideoNodeId))
                    {
                        // Map child -> parent for the back button
                        if (!parentMap.ContainsKey(choice.nextVideoNodeId))
                        {
                            parentMap.Add(choice.nextVideoNodeId, node.id);
                        }
                    }
                }
            }
            
            // Fallback: If no node has isStartingNode, try to find a node named "start"
            if (string.IsNullOrEmpty(startingNodeId) && videoNodes.ContainsKey("start"))
            {
                startingNodeId = "start";
                Debug.LogWarning("[Parse] No node marked 'isStartingNode'. Falling back to node ID 'start'.");
            }
             // Fallback 2: Just take the first node in the list
            else if (string.IsNullOrEmpty(startingNodeId) && manifest.nodes.Count > 0)
            {
                startingNodeId = manifest.nodes[0].id;
                Debug.LogWarning($"[Parse] No start node found. Defaulting to first node: {startingNodeId}");
            }

        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Parse] Error: {ex.Message}");
            videoNodes = new Dictionary<string, VideoNode>();
        }
    }

    // --- VIDEO LOGIC ---

    public void LoadStartingNode()
    {
        LoadNode(startingNodeId);
    }

    public void GoToPreviousNode()
    {
        if (parentMap.TryGetValue(currentNodeId, out string parentNodeId))
        {
            LoadNode(parentNodeId);
        }
    }

    public void LoadNode(string nodeId)
    {
        if (videoNodes == null || !videoNodes.ContainsKey(nodeId)) return;

        videoPlayer.Stop();
        videoPlayer.prepareCompleted -= PlayPreparedVideo;

        currentNodeId = nodeId;
        VideoNode currentNode = videoNodes[nodeId];

        // Resolve Video Path
        string videoPath = Path.Combine(dataFolderPath, currentNode.videoFileName);
        
        if (!File.Exists(videoPath))
        {
            Debug.LogError($"[Video] Missing file: {videoPath}");
            return;
        }

        string videoUrl = videoPath;
        if (Application.platform == RuntimePlatform.Android && !videoUrl.StartsWith("file://"))
        {
            videoUrl = "file://" + videoUrl;
        }

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = videoUrl;
        videoPlayer.Prepare();
        videoPlayer.prepareCompleted += PlayPreparedVideo;

        UpdateVideoUI(currentNode);
    }

    private void UpdateVideoUI(VideoNode currentNode)
    {
        foreach (Transform child in buttonContainer) Destroy(child.gameObject);

        // Back Button
        bool canGoBack = parentMap.ContainsKey(currentNodeId) && currentNodeId != startingNodeId;
        if (backButtonPrefab != null && canGoBack)
        {
            Button backBtn = Instantiate(backButtonPrefab, buttonContainer);
            TextMeshProUGUI btnText = backBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = "Go Back";
            backBtn.onClick.AddListener(GoToPreviousNode);
        }

        // Choice Buttons
        if (currentNode.choices != null)
        {
            foreach (Choice choice in currentNode.choices)
            {
                if (string.IsNullOrEmpty(choice.nextVideoNodeId) || !videoNodes.ContainsKey(choice.nextVideoNodeId))
                    continue;

                Button choiceBtn = Instantiate(choiceButtonPrefab, buttonContainer);
                TextMeshProUGUI btnText = choiceBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null) btnText.text = choice.buttonTitle;

                string targetId = choice.nextVideoNodeId;
                choiceBtn.onClick.AddListener(() => LoadNode(targetId));
            }
        }
    }

    private void PlayPreparedVideo(VideoPlayer source)
    {
        source.Play();
        source.prepareCompleted -= PlayPreparedVideo;
    }

    private void HandleVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[Video Error] {message}");
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= PlayPreparedVideo;
            videoPlayer.errorReceived -= HandleVideoError;
        }
    }
}