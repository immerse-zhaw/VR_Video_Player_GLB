using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;


using TMPro;
public class GLBImporter : MonoBehaviour
{
    public Transform spawn;
    public float modelScale = 1.0f;
    private string glbDirectory;
    public TextMeshProUGUI statusText; // Assign this in Unity inspector

    void Awake()
    {
        // Use Application.persistentDataPath/Content/GLB for GLB storage
        glbDirectory = Path.Combine(Application.persistentDataPath, "Content", "GLB");
        EnsureGLBDirectoryExists();
        if (statusText != null)
            statusText.text = "Ready to import GLB.";
    }

    private void EnsureGLBDirectoryExists()
    {
        try
        {
            string contentPath = Path.Combine(Application.persistentDataPath, "Content");
            string glbPath = Path.Combine(contentPath, "GLB");
            
            if (!Directory.Exists(contentPath))
            {
                Directory.CreateDirectory(contentPath);
                Debug.Log($"Created Content directory: {contentPath}");
            }
            
            if (!Directory.Exists(glbPath))
            {
                Directory.CreateDirectory(glbPath);
                Debug.Log($"Created GLB directory: {glbPath}");
            }
            
            Debug.Log($"GLB directory ready: {glbDirectory}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to create GLB directory: {ex.Message}");
        }
    }

    public async void ImportGLB(string fileName)
    {
        if (statusText != null)
            statusText.text = "Importing...";
        string fullPath = Path.Combine(glbDirectory, fileName);
        if (!File.Exists(fullPath))
        {
            // Try alternate extension if not found
            string altExt = Path.GetExtension(fileName).ToLower() == ".glb" ? ".gltf" : ".glb";
            string altName = Path.GetFileNameWithoutExtension(fileName) + altExt;
            string altPath = Path.Combine(glbDirectory, altName);
            if (File.Exists(altPath))
            {
                Debug.Log($"File not found as {fileName}, but found as {altName}. Using that.");
                fullPath = altPath;
            }
            else
            {
                Debug.LogError($"GLB/GLTF file not found: {fullPath} or {altPath}");
                if (statusText != null)
                    statusText.text = "File not found.";
                return;
            }
        }

        GltfImport gltf = new GltfImport();
        bool success = await gltf.Load(fullPath);
        if (success)
        {
            GameObject model = new GameObject("GLB_Model");
            await gltf.InstantiateMainSceneAsync(model.transform);

            // Spawn at spawn transform
            if (spawn != null)
            {
                model.transform.localScale = Vector3.one * modelScale;
                model.transform.position = spawn.position;
                model.transform.rotation = spawn.rotation;
            }
            if (statusText != null)
                statusText.text = "Import successful!";

            // Add Rigidbody
            var rigidbody = model.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = model.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;

            // Add Collider
            if (!model.TryGetComponent<Collider>(out _))
            {
                var meshFilter = model.GetComponentInChildren<MeshFilter>();
                if (meshFilter != null)
                {
                    var meshCollider = model.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                    meshCollider.convex = true;
                }
                else
                {
                    var boxCollider = model.AddComponent<BoxCollider>();
                    var bounds = CalculateBounds(model);
                    boxCollider.center = bounds.center;
                    boxCollider.size = bounds.size;
                }
            }

            // Add Grab interaction
            var grabbable = model.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            grabbable.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.VelocityTracking;
            grabbable.throwOnDetach = true;

            // Add Editable Tag
            model.tag = "Editable";

            // Add Mesh Renderer to root if needed
            var childMeshRenderer = model.GetComponentInChildren<MeshRenderer>();
            var childMeshFilter = model.GetComponentInChildren<MeshFilter>();
            if (childMeshRenderer != null && childMeshFilter != null)
            {
                var meshFilter = model.AddComponent<MeshFilter>();
                var meshRenderer = model.AddComponent<MeshRenderer>();
                meshFilter.sharedMesh = childMeshFilter.sharedMesh;
                meshRenderer.sharedMaterials = childMeshRenderer.sharedMaterials;
            }

            // Set glTF-unlit shader for all MeshRenderers
            var meshRenderers = model.GetComponentsInChildren<MeshRenderer>();
            Shader gltfUnlitShader = Shader.Find("Shader Graphs/glTF-unlit");
            if (gltfUnlitShader != null)
            {
                foreach (var mr in meshRenderers)
                {
                    foreach (var mat in mr.materials)
                    {
                        mat.shader = gltfUnlitShader;
                    }
                }
            }

            Debug.Log("Model loaded in scene.");
        }
        else
        {
            if (statusText != null)
                statusText.text = "Import failed.";
            Debug.LogError("Failed to load GLB file: " + fileName);
        }
    }

    private Bounds CalculateBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds;
    }
}
