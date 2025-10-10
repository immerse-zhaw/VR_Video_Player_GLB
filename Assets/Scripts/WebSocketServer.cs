using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Fleck;

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
        string folderPath = Path.Combine(Application.persistentDataPath, "SampleVids");
        if (!Directory.Exists(folderPath))
        {
            Debug.LogWarning($"SampleVids folder not found: {folderPath}");
            socket.Send("{\"type\":\"videoList\",\"files\":[]}");
            return;
        }
        var files = Directory.GetFiles(folderPath, "*.mp4");
        var fullPaths = new List<string>();
        foreach (var file in files)
            fullPaths.Add("file:///" + file.Replace("\\", "/"));
        var response = new VideoListResponse { type = "videoList", files = fullPaths.ToArray() };
        socket.Send(JsonUtility.ToJson(response));
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
}
