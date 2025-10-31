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
                    EnqueueOnMainThread(() => HandleDownloadFile(cmd.url, cmd.filename, cmd.folder));
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

    private void HandleDownloadFile(string url, string filename, string folder)
    {
        Debug.Log($"Download request: {filename} from {url} to folder {folder}");
        StartCoroutine(DownloadFileCoroutine(url, filename, folder));
    }

    private System.Collections.IEnumerator DownloadFileCoroutine(string url, string filename, string folder)
    {
    string saveFolder = Path.Combine(Application.persistentDataPath, "Content", "Videos");
    Directory.CreateDirectory(saveFolder);
    string savePath = Path.Combine(saveFolder, filename);

        using (UnityEngine.Networking.UnityWebRequest uwr = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            yield return uwr.SendWebRequest();
            if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                File.WriteAllBytes(savePath, uwr.downloadHandler.data);
                Debug.Log($"File downloaded and saved to: {savePath}");
            }
            else
            {
                Debug.LogError($"Download failed: {uwr.error}");
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
}