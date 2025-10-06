using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Fleck;

public class WebSocketServer : MonoBehaviour
{
    public VideoManager videoManager;
    private Fleck.WebSocketServer server;
    private Queue<Action> mainThreadActions = new Queue<Action>();

    void Start()
    {
        FleckLog.Level = LogLevel.Warn;
        server = new Fleck.WebSocketServer("ws://0.0.0.0:8080");
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
                    lock (mainThreadActions)
                    {
                        if (cmd.action == "play" && !string.IsNullOrEmpty(cmd.path))
                            mainThreadActions.Enqueue(() => videoManager.PlayVideo(cmd.path));
                        else if (cmd.action == "pause")
                            mainThreadActions.Enqueue(() => videoManager.PauseVideo());
                        else if (cmd.action == "resume")
                            mainThreadActions.Enqueue(() => videoManager.ResumeVideo());
                        else if (cmd.action == "toggleMode")
                            mainThreadActions.Enqueue(() => videoManager.ToggleVideoMode(cmd.mode == "360"));
                        else if (cmd.action == "listVideos")
                            mainThreadActions.Enqueue(() => SendVideoList(socket));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("WebSocket command error: " + ex.Message);
                }
            };
        });
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

    [Serializable]
    internal class WSCommand
    {
        public string action;
        public string path;
        public string mode;
    }

    [Serializable]
    internal class VideoListResponse
    {
        public string type;
        public string[] files;
    }
}
