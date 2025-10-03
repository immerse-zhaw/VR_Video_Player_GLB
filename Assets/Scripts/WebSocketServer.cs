using System;
using System.Collections.Generic;
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
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("WebSocket command error: " + ex.Message);
                }
            };
        });
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
    private class WSCommand
    {
        public string action;
        public string path;
        public string mode;
    }
}
