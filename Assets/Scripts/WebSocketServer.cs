using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Fleck;

/// <summary>
/// WebSocket server for VR Video Player that handles remote control commands.
/// 
/// Supported commands:
/// - play: Play a video file
/// - pause: Pause current video playback
/// - resume: Resume paused video playback
/// - toggleMode: Switch between 2D and 360 video modes
/// - seek: Seek to specific time in video
/// - listVideos: Get list of available videos
/// - importGLB: Import GLB/GLTF 3D models
/// </summary>
public class WebSocketServer : MonoBehaviour
{
    public VideoManager videoManager;
    public GLBImporter glbImporter;
    private Fleck.WebSocketServer server;
    private Queue<Action> mainThreadActions = new Queue<Action>();

    void Start()
    {
        FleckLog.Level = LogLevel.Warn;
        server = new Fleck.WebSocketServer("ws://0.0.0.0:8080");
        if (glbImporter == null)
            glbImporter = FindFirstObjectByType<GLBImporter>();

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
            case "listGLBs":
                EnqueueOnMainThread(() => SendGLBList(socket));
                break;
            case "importGLB":
                if (!string.IsNullOrEmpty(cmd.name))
                    EnqueueOnMainThread(() => HandleImportGLB(cmd.name));
                break;
            case "downloadFile":
                if (!string.IsNullOrEmpty(cmd.url) && !string.IsNullOrEmpty(cmd.filename))
                    EnqueueOnMainThread(() => HandleDownloadFile(socket, cmd.url, cmd.filename, cmd.folder));
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
            glbImporter = FindFirstObjectByType<GLBImporter>();
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
        string folderPath = Path.Combine(Application.persistentDataPath, "Content", "Videos");
        if (!Directory.Exists(folderPath))
        {
            Debug.LogWarning($"Videos folder not found: {folderPath}");
            socket.Send("{\"type\":\"videoList\",\"files\":[]}");
            return;
        }
        
        // Support multiple video formats
        var videoExtensions = new[] { "*.mp4", "*.mov", "*.avi", "*.mkv", "*.webm" };
        var files = new List<string>();
        foreach (var ext in videoExtensions)
        {
            files.AddRange(Directory.GetFiles(folderPath, ext));
        }
        
        var fullPaths = new List<string>();
        foreach (var file in files)
        {
            // Only add file:// prefix for local files, not HTTP URLs
            if (file.StartsWith("http://") || file.StartsWith("https://"))
                fullPaths.Add(file);
            else
                fullPaths.Add("file:///" + file.Replace("\\", "/"));
        }
        var response = new VideoListResponse { type = "videoList", files = fullPaths.ToArray() };
        socket.Send(JsonUtility.ToJson(response));
    }

    private void SendGLBList(IWebSocketConnection socket)
    {
        string folderPath = Path.Combine(Application.persistentDataPath, "Content", "GLB");
        if (!Directory.Exists(folderPath))
        {
            Debug.LogWarning($"GLB folder not found: {folderPath}");
            socket.Send("{\"type\":\"glbList\",\"files\":[]}");
            return;
        }
        
        var glbExtensions = new[] { "*.glb", "*.gltf" };
        var files = new List<string>();
        foreach (var ext in glbExtensions)
        {
            files.AddRange(Directory.GetFiles(folderPath, ext));
        }
        
        var fullPaths = new List<string>();
        foreach (var file in files)
        {
            // Only add file:// prefix for local files, not HTTP URLs
            if (file.StartsWith("http://") || file.StartsWith("https://"))
                fullPaths.Add(file);
            else
                fullPaths.Add("file:///" + file.Replace("\\", "/"));
        }
        var response = new GLBListResponse { type = "glbList", files = fullPaths.ToArray() };
        socket.Send(JsonUtility.ToJson(response));
    }

    private void HandleDownloadFile(IWebSocketConnection socket, string url, string filename, string folder)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(filename))
        {
            Debug.LogWarning("Download request missing url or filename");
            return;
        }

        string safeFileName = Path.GetFileName(filename);
        string targetDirectory = ResolveDownloadDirectory(folder);

        try
        {
            Directory.CreateDirectory(targetDirectory);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to prepare download folder '{targetDirectory}': {ex.Message}");
            SendDownloadStatus(socket, "failed", safeFileName, folder, 0f, "Unable to create target directory");
            return;
        }

        Debug.Log($"Download request: {safeFileName} from {url} to folder {targetDirectory}");
        StartCoroutine(DownloadFileCoroutine(socket, url, safeFileName, targetDirectory, folder));
    }

    private System.Collections.IEnumerator DownloadFileCoroutine(IWebSocketConnection socket, string url, string filename, string targetDirectory, string folderLabel)
    {
        string savePath = Path.Combine(targetDirectory, filename);
        SendDownloadStatus(socket, "started", filename, folderLabel, 0f, "Starting download");

        using (UnityEngine.Networking.UnityWebRequest uwr = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            var asyncOperation = uwr.SendWebRequest();
            float lastReported = -0.1f;

            while (!asyncOperation.isDone)
            {
                float progress = Mathf.Clamp01(uwr.downloadProgress);
                if (progress - lastReported >= 0.05f)
                {
                    lastReported = progress;
                    SendDownloadStatus(socket, "progress", filename, folderLabel, progress * 100f, null);
                }
                yield return null;
            }

            float finalProgress = Mathf.Clamp01(uwr.downloadProgress) * 100f;

            if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                try
                {
                    File.WriteAllBytes(savePath, uwr.downloadHandler.data);
                    Debug.Log($"File downloaded and saved to: {savePath}");
                    SendDownloadStatus(socket, "completed", filename, folderLabel, 100f, "Download complete");

                    if (string.Equals(folderLabel, "GLB", StringComparison.OrdinalIgnoreCase))
                    {
                        SendGLBList(socket);
                    }
                    else
                    {
                        SendVideoList(socket);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to save downloaded file: {ex.Message}");
                    SendDownloadStatus(socket, "failed", filename, folderLabel, finalProgress, "Failed to save file");
                }
            }
            else
            {
                string errorMessage = string.IsNullOrEmpty(uwr.error) ? "Unknown download error" : uwr.error;
                Debug.LogError($"Download failed: {errorMessage}");
                SendDownloadStatus(socket, "failed", filename, folderLabel, finalProgress, errorMessage);
            }
        }
    }

    private string ResolveDownloadDirectory(string folder)
    {
        string contentRoot = Path.Combine(Application.persistentDataPath, "Content");

        if (string.Equals(folder, "GLB", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(contentRoot, "GLB");
        }

        return Path.Combine(contentRoot, "Videos");
    }

    private void SendDownloadStatus(IWebSocketConnection socket, string state, string filename, string folder, float progress, string message)
    {
        if (socket == null)
            return;

        var response = new DownloadStatusResponse
        {
            type = "downloadStatus",
            state = state,
            filename = filename,
            folder = folder,
            progress = progress,
            message = message
        };

        try
        {
            socket.Send(JsonUtility.ToJson(response));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to send download status update: {ex.Message}");
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
        public string url;
        public string filename;
        public string folder;
    }

    [Serializable]
    internal class VideoListResponse
    {
        public string type;
        public string[] files;
    }

    [Serializable]
    internal class GLBListResponse
    {
        public string type;
        public string[] files;
    }

    [Serializable]
    internal class DownloadStatusResponse
    {
        public string type;
        public string state;
        public string filename;
        public string folder;
        public float progress;
        public string message;
    }
}