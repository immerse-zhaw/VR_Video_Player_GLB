using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Fleck;

/// <summary>
/// WebSocket server for VR Video Player that handles remote control commands.
/// 
/// New Folder Structure Feature:
/// - Sends complete folder structure from headset root directory
/// - Use browser console: getFolderStructure() to request folder structure
/// - Use browser console: listConnectedDevices() to see connected devices
/// - Folder structure is displayed in both Unity console and browser console
/// </summary>
public class WebSocketServer : MonoBehaviour
{
    public VideoManager videoManager;
    public GLBImporter glbImporter;
    private Fleck.WebSocketServer server;
    private Queue<Action> mainThreadActions = new Queue<Action>();

    void Start()
    {
        // Request Android permissions on start
        RequestAndroidPermissions();
        
        FleckLog.Level = LogLevel.Warn;
        server = new Fleck.WebSocketServer("ws://0.0.0.0:8080");
        if (glbImporter == null)
            glbImporter = FindObjectOfType<GLBImporter>();

        server.Start(socket =>
        {
            socket.OnOpen = () => Debug.Log("WebSocket client connected");
            socket.OnClose = () => Debug.Log("WebSocket client disconnected");
            socket.OnMessage = message =>
            {
                Debug.Log("WebSocket received: " + message);
                try
                {
                    var cmd = JsonUtility.FromJson<WSCommand>(message);
                    HandleCommand(cmd, socket);
                }
                catch (Exception ex)
                {
                    Debug.LogError("WebSocket command error: " + ex.Message);
                }
            };
        });
    }

    private void HandleCommand(WSCommand cmd, IWebSocketConnection socket)
    {
        if (cmd == null || string.IsNullOrEmpty(cmd.action))
        {
            Debug.LogWarning("Received invalid WebSocket command.");
            return;
        }

        switch (cmd.action)
        {
            case "play":
                if (!string.IsNullOrEmpty(cmd.path))
                    EnqueueOnMainThread(() => videoManager?.PlayVideo(cmd.path));
                break;
            case "pause":
                EnqueueOnMainThread(() => videoManager?.PauseVideo());
                break;
            case "resume":
                EnqueueOnMainThread(() => videoManager?.ResumeVideo());
                break;
            case "toggleMode":
                EnqueueOnMainThread(() => videoManager?.ToggleVideoMode(cmd.mode == "360"));
                break;
            case "seek":
                EnqueueOnMainThread(() => videoManager?.SeekVideo(cmd.time));
                break;
            case "listVideos":
                EnqueueOnMainThread(() => SendVideoList(socket));
                break;
            case "getFolderStructure":
                EnqueueOnMainThread(() => SendFolderStructure(socket));
                break;
            case "browseFolder":
                if (!string.IsNullOrEmpty(cmd.path))
                    EnqueueOnMainThread(() => BrowseSpecificFolder(cmd.path, socket));
                break;
            case "getAccessibleFolders":
                EnqueueOnMainThread(() => SendAccessibleFolders(socket));
                break;
            case "requestPermissions":
                EnqueueOnMainThread(() => {
                    RequestAndroidPermissions();
                    SendDebugLog("🔐 Android storage permissions requested", socket);
                });
                break;
            case "importGLB":
                if (!string.IsNullOrEmpty(cmd.name))
                    EnqueueOnMainThread(() => HandleImportGLB(cmd.name));
                break;
            default:
                Debug.LogWarning($"Unhandled WebSocket action: {cmd.action}");
                break;
        }
    }

    private void HandleImportGLB(string fileName)
    {
        if (glbImporter == null)
        {
            glbImporter = FindObjectOfType<GLBImporter>();
            if (glbImporter == null)
            {
                Debug.LogError("GLBImporter not found in scene!");
                return;
            }
        }

        glbImporter.ImportGLB(fileName);
        Debug.Log($"Requested import of GLB/GLTF: {fileName}");
    }

    private void EnqueueOnMainThread(Action action)
    {
        if (action == null) return;
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    private void SendVideoList(IWebSocketConnection socket)
    {
        // Adjust path for build/runtime if needed
        string folderPath = "/sdcard";
        if (!Directory.Exists(folderPath))
        {
            Debug.LogWarning($"/sdcard folder not found: {folderPath}");
            socket.Send("{\"type\":\"videoList\",\"files\":[]}");
            return;
        }
        // Support multiple video extensions
        var videoExtensions = new[] { "*.mp4", "*.mov", "*.avi", "*.mkv", "*.webm" };
        var files = new List<string>();
        foreach (var ext in videoExtensions)
            files.AddRange(Directory.GetFiles(folderPath, ext));
        var fullPaths = new List<string>();
        foreach (var file in files)
            fullPaths.Add("file:///" + file.Replace("\\", "/"));
        var response = new VideoListResponse { type = "videoList", files = fullPaths.ToArray() };
        socket.Send(JsonUtility.ToJson(response));
    }

    private void SendFolderStructure(IWebSocketConnection socket)
    {
        // Android-specific accessible paths for VR headsets
        string[] accessiblePaths = {
            "/storage/emulated/0", // Modern Android primary storage
            "/sdcard",             // Legacy symlink to primary storage
            Application.persistentDataPath, // App's private storage
            Application.streamingAssetsPath, // App's streaming assets
            "/storage/emulated/0/Android/data", // Shared Android data (might need permissions)
            "/storage/emulated/0/Download", // Downloads folder
            "/storage/emulated/0/DCIM", // Camera folder
            "/storage/emulated/0/Movies", // Movies folder
            "/storage/emulated/0/Pictures", // Pictures folder
        };
        
        string rootPath = "/storage/emulated/0"; // Default modern Android path
        
        SendDebugLog("🔍 Testing Android file system access...", socket);
        
        // Test which paths are accessible
        foreach (var path in accessiblePaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var testFiles = Directory.GetFileSystemEntries(path);
                    SendDebugLog($"✅ Accessible: {path} ({testFiles.Length} entries)", socket);
                    if (path.Contains("/storage/emulated/0") && !path.Contains("Android/data"))
                    {
                        rootPath = path; // Prefer main storage paths
                        break;
                    }
                }
                else
                {
                    SendDebugLog($"❌ Not found: {path}", socket);
                }
            }
            catch (UnauthorizedAccessException)
            {
                SendDebugLog($"🔒 Access denied: {path}", socket);
            }
            catch (Exception ex)
            {
                SendDebugLog($"❌ Error testing {path}: {ex.Message}", socket);
            }
        }

        SendDebugLog($"🔍 Scanning folder structure from root: {rootPath}", socket);
        
        try
        {
            var folderStructure = ScanDirectory(rootPath, 0, 3, socket); // Pass socket for debug logging
            var response = new FolderStructureResponse 
            { 
                type = "folderStructure", 
                rootPath = rootPath,
                structure = folderStructure
            };
            
            string jsonResponse = JsonUtility.ToJson(response);
            socket.Send(jsonResponse);
            
            SendDebugLog("=== FOLDER STRUCTURE COMPLETE ===", socket);
            SendDebugLog($"📍 Root Path: {rootPath}", socket);
            SendDebugLog($"📊 Structure sent with {CountItems(folderStructure)} total items", socket);
            SendDebugLog("================================", socket);
        }
        catch (Exception ex)
        {
            SendDebugLog($"❌ Error scanning folder structure: {ex.Message}", socket);
            var errorResponse = new FolderStructureResponse 
            { 
                type = "folderStructure", 
                rootPath = rootPath,
                structure = new FolderInfo { name = "ERROR", path = rootPath, isDirectory = true, children = new FolderInfo[0] }
            };
            socket.Send(JsonUtility.ToJson(errorResponse));
        }
    }

    private void RequestAndroidPermissions()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("🔐 Requesting Android storage permissions...");
        
        // Request storage permissions for Android
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.ExternalStorageRead))
        {
            Debug.Log("📖 Requesting READ_EXTERNAL_STORAGE permission");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.ExternalStorageRead);
        }
        else
        {
            Debug.Log("📖 READ_EXTERNAL_STORAGE already granted");
        }
        
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.ExternalStorageWrite))
        {
            Debug.Log("📝 Requesting WRITE_EXTERNAL_STORAGE permission");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.ExternalStorageWrite);
        }
        else
        {
            Debug.Log("📝 WRITE_EXTERNAL_STORAGE already granted");
        }
        
        Debug.Log("ℹ️ AndroidManifest.xml should include requestLegacyExternalStorage=true for Quest compatibility");
        #else
        Debug.Log("ℹ️ Android permissions only work on device, not in editor");
        #endif
    }

    private void SendDebugLog(string message, IWebSocketConnection socket)
    {
        var debugResponse = new DebugLogResponse
        {
            type = "debugLog",
            message = message,
            timestamp = System.DateTime.Now.ToString("HH:mm:ss")
        };
        socket.Send(JsonUtility.ToJson(debugResponse));
        Debug.Log(message); // Also log to Unity console
    }

    private void SendAccessibleFolders(IWebSocketConnection socket)
    {
        SendDebugLog("🔍 Scanning for accessible Android folders...", socket);
        
        var accessibleFolders = new List<FolderInfo>();
        
        // Common Android accessible paths
        string[] androidPaths = {
            "/storage/emulated/0",
            "/storage/emulated/0/Download", 
            "/storage/emulated/0/DCIM",
            "/storage/emulated/0/Pictures",
            "/storage/emulated/0/Movies", 
            "/storage/emulated/0/Music",
            "/storage/emulated/0/Documents",
            "/storage/emulated/0/Podcasts",
            "/storage/emulated/0/Ringtones",
            "/storage/emulated/0/Alarms",
            "/storage/emulated/0/Notifications",
            "/storage/emulated/0/Android/data",
            "/storage/emulated/0/Android/media",
            "/sdcard",
            Application.persistentDataPath,
            Application.temporaryCachePath
        };

        foreach (var path in androidPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var entries = Directory.GetFileSystemEntries(path);
                    accessibleFolders.Add(new FolderInfo
                    {
                        name = $"✅ {path} ({entries.Length} items)",
                        path = path,
                        isDirectory = true,
                        children = new FolderInfo[0],
                        size = 0
                    });
                    SendDebugLog($"✅ Accessible: {path} with {entries.Length} items", socket);
                }
            }
            catch (UnauthorizedAccessException)
            {
                accessibleFolders.Add(new FolderInfo
                {
                    name = $"🔒 {path} (Access Denied)",
                    path = path,
                    isDirectory = true,
                    children = new FolderInfo[0],
                    size = 0
                });
                SendDebugLog($"🔒 Access denied: {path}", socket);
            }
            catch (Exception ex)
            {
                SendDebugLog($"❌ Error testing {path}: {ex.Message}", socket);
            }
        }

        var response = new FolderContentsResponse
        {
            type = "folderContents",
            path = "Android Accessible Folders",
            exists = true,
            contents = accessibleFolders.ToArray()
        };

        socket.Send(JsonUtility.ToJson(response));
        SendDebugLog($"📊 Found {accessibleFolders.Count} total paths ({accessibleFolders.Count(f => f.name.StartsWith("✅"))} accessible)", socket);
    }

    private void BrowseSpecificFolder(string folderPath, IWebSocketConnection socket)
    {
        SendDebugLog($"🔍 Browsing specific folder: {folderPath}", socket);
        
        // Test different approaches to file access
        try
        {
            // Test 1: Check if we can enumerate directory entries
            var allEntries = Directory.EnumerateFileSystemEntries(folderPath);
            SendDebugLog($"📊 EnumerateFileSystemEntries found: {allEntries.Count()} total entries", socket);
            
            // Test 2: Try with search pattern  
            var allFiles = Directory.EnumerateFiles(folderPath, "*");
            SendDebugLog($"📄 EnumerateFiles with * pattern found: {allFiles.Count()} files", socket);
            
            // Test 3: Try manual directory info approach
            var dirInfo = new DirectoryInfo(folderPath);
            var fileInfos = dirInfo.GetFiles();
            SendDebugLog($"📂 DirectoryInfo.GetFiles() found: {fileInfos.Length} files", socket);
            
            // Test 4: Check specific common file extensions
            string[] commonExtensions = { "*.txt", "*.mp4", "*.jpg", "*.png", "*.pdf", "*.zip", "*.apk" };
            foreach (var pattern in commonExtensions)
            {
                var matchingFiles = Directory.GetFiles(folderPath, pattern);
                if (matchingFiles.Length > 0)
                {
                    SendDebugLog($"🎯 Found {matchingFiles.Length} files matching {pattern}: {string.Join(", ", matchingFiles.Select(Path.GetFileName))}", socket);
                }
            }
        }
        catch (Exception testEx)
        {
            SendDebugLog($"❌ File enumeration test failed: {testEx.Message}", socket);
        }
        
        try
        {
            if (!Directory.Exists(folderPath))
            {
                Debug.LogWarning($"Folder does not exist: {folderPath}");
                var errorResponse = new FolderContentsResponse 
                { 
                    type = "folderContents", 
                    path = folderPath,
                    exists = false,
                    contents = new FolderInfo[0]
                };
                socket.Send(JsonUtility.ToJson(errorResponse));
                return;
            }

            var contents = new List<FolderInfo>();
            
            // Add directories first
            var directories = Directory.GetDirectories(folderPath);
            foreach (var dir in directories)
            {
                try
                {
                    contents.Add(new FolderInfo
                    {
                        name = Path.GetFileName(dir),
                        path = dir,
                        isDirectory = true,
                        children = new FolderInfo[0] // Don't scan children for performance
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    contents.Add(new FolderInfo 
                    { 
                        name = Path.GetFileName(dir) + " (Access Denied)", 
                        path = dir, 
                        isDirectory = true, 
                        children = new FolderInfo[0] 
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error accessing directory {dir}: {ex.Message}");
                }
            }
            
            // Add files
            SendDebugLog($"📁 Attempting to get files from: {folderPath}", socket);
            var files = Directory.GetFiles(folderPath);
            SendDebugLog($"📄 Directory.GetFiles returned {files.Length} files", socket);
            
            if (files.Length > 0)
            {
                SendDebugLog($"📋 Files found: {string.Join(", ", files.Select(Path.GetFileName))}", socket);
            }
            else
            {
                SendDebugLog("❌ No files found in this directory", socket);
            }
            
            foreach (var file in files)
            {
                try
                {
                    SendDebugLog($"🔄 Processing file: {Path.GetFileName(file)}", socket);
                    var fileInfo = new FileInfo(file);
                    var folderInfoItem = new FolderInfo
                    {
                        name = Path.GetFileName(file),
                        path = file,
                        isDirectory = false,
                        children = new FolderInfo[0],
                        size = fileInfo.Length
                    };
                    contents.Add(folderInfoItem);
                    SendDebugLog($"✅ Added file to contents: {folderInfoItem.name}, Size: {FormatFileSize(folderInfoItem.size)}, IsDirectory: {folderInfoItem.isDirectory}", socket);
                }
                catch (Exception ex)
                {
                    SendDebugLog($"❌ Error accessing file {Path.GetFileName(file)}: {ex.Message}", socket);
                }
            }

            SendDebugLog($"📦 Final contents array has {contents.Count} items", socket);
            foreach (var item in contents.Take(5)) // Show first 5 items to avoid spam
            {
                SendDebugLog($"📋 Content item: {item.name}, IsDirectory: {item.isDirectory}, Size: {FormatFileSize(item.size)}", socket);
            }
            if (contents.Count > 5)
            {
                SendDebugLog($"... and {contents.Count - 5} more items", socket);
            }
            
            var response = new FolderContentsResponse 
            { 
                type = "folderContents", 
                path = folderPath,
                exists = true,
                contents = contents.ToArray()
            };
            
            string jsonResponse = JsonUtility.ToJson(response);
            SendDebugLog($"🚀 Sending response with {contents.Count} items", socket);
            socket.Send(jsonResponse);

            // Log to console for visibility
            Debug.Log($"=== FOLDER CONTENTS: {folderPath} ===");
            Debug.Log($"Found {contents.Count} items ({directories.Length} folders, {files.Length} files)");
            Debug.Log($"Directories found: {string.Join(", ", directories.Select(d => Path.GetFileName(d)))}");
            Debug.Log($"Files found: {string.Join(", ", files.Select(f => Path.GetFileName(f)))}");
            
            foreach (var item in contents.Take(10)) // Show first 10 items
            {
                string icon = item.isDirectory ? "📁" : "📄";
                string sizeInfo = item.isDirectory ? "" : $" ({FormatFileSize(item.size)})";
                Debug.Log($"{icon} {item.name}{sizeInfo} [Path: {item.path}]");
            }
            if (contents.Count > 10)
                Debug.Log($"... and {contents.Count - 10} more items");
            Debug.Log("=====================================");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error browsing folder {folderPath}: {ex.Message}");
            var errorResponse = new FolderContentsResponse 
            { 
                type = "folderContents", 
                path = folderPath,
                exists = false,
                contents = new FolderInfo[0]
            };
            socket.Send(JsonUtility.ToJson(errorResponse));
        }
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{num} {suffixes[place]}";
    }

    private int CountItems(FolderInfo folder)
    {
        int count = 1; // Count the folder itself
        if (folder.children != null)
        {
            foreach (var child in folder.children)
            {
                count += CountItems(child);
            }
        }
        return count;
    }

    private FolderInfo ScanDirectory(string path, int currentDepth, int maxDepth, IWebSocketConnection socket = null)
    {
        var info = new FolderInfo
        {
            name = Path.GetFileName(path),
            path = path,
            isDirectory = true,
            children = new FolderInfo[0]
        };

        if (string.IsNullOrEmpty(info.name))
            info.name = path; // For root directories like "/"

        if (currentDepth >= maxDepth)
        {
            if (socket != null) SendDebugLog($"📏 Reached max depth {maxDepth} at: {path}", socket);
            return info;
        }

        try
        {
            var children = new List<FolderInfo>();
            
            // Add directories first
            var directories = Directory.GetDirectories(path);
            if (socket != null) SendDebugLog($"📁 Found {directories.Length} directories in: {path}", socket);
            
            foreach (var dir in directories)
            {
                try
                {
                    children.Add(ScanDirectory(dir, currentDepth + 1, maxDepth, socket));
                }
                catch (UnauthorizedAccessException)
                {
                    if (socket != null) SendDebugLog($"🔒 Access denied to directory: {Path.GetFileName(dir)}", socket);
                    children.Add(new FolderInfo 
                    { 
                        name = Path.GetFileName(dir) + " (Access Denied)", 
                        path = dir, 
                        isDirectory = true, 
                        children = new FolderInfo[0] 
                    });
                }
                catch (Exception ex)
                {
                    if (socket != null) SendDebugLog($"❌ Error accessing directory {Path.GetFileName(dir)}: {ex.Message}", socket);
                }
            }
            
            // Add files
            var files = Directory.GetFiles(path);
            if (socket != null) SendDebugLog($"📄 Found {files.Length} files in: {path}", socket);
            
            foreach (var file in files)
            {
                if (socket != null) SendDebugLog($"📋 Adding file: {Path.GetFileName(file)}", socket);
                children.Add(new FolderInfo
                {
                    name = Path.GetFileName(file),
                    path = file,
                    isDirectory = false,
                    children = new FolderInfo[0],
                    size = new FileInfo(file).Length
                });
            }
            
            info.children = children.ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            Debug.LogWarning($"Access denied to directory: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error scanning directory {path}: {ex.Message}");
        }

        return info;
    }

    private void LogFolderStructure(FolderInfo folder, string indent)
    {
        string icon = folder.isDirectory ? "📁" : "📄";
        Debug.Log($"{indent}{icon} {folder.name}");
        
        if (folder.children != null)
        {
            foreach (var child in folder.children)
            {
                LogFolderStructure(child, indent + "  ");
            }
        }
    }

    void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue().Invoke();
        }
    }

    void OnDestroy()
    {
        server?.Dispose();
        server = null;
    }

    [Serializable]
    internal class WSCommand
    {
        public string action;
        public string path;
        public string mode;
        public double time;
        public string name;
    }

    [Serializable]
    internal class VideoListResponse
    {
        public string type;
        public string[] files;
    }

    [Serializable]
    internal class FolderStructureResponse
    {
        public string type;
        public string rootPath;
        public FolderInfo structure;
    }

    [Serializable]
    internal class FolderInfo
    {
        public string name;
        public string path;
        public bool isDirectory;
        public FolderInfo[] children;
        public long size; // File size in bytes
    }

    [Serializable]
    internal class FolderContentsResponse
    {
        public string type;
        public string path;
        public bool exists;
        public FolderInfo[] contents;
    }

    [Serializable]
    internal class DebugLogResponse
    {
        public string type;
        public string message;
        public string timestamp;
    }
}
