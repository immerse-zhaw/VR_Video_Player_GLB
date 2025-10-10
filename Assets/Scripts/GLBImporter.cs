using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;


public class GLBImporter : MonoBehaviour
{
    public Transform spawn;
    public float modelScale = 1.0f;
    private string glbDirectory;

    void Awake()
    {
        glbDirectory = Path.Combine(Application.persistentDataPath, "GLB_Objects");
        if (!Directory.Exists(glbDirectory))
        {
            Directory.CreateDirectory(glbDirectory);
            Debug.Log($"Created missing folder: {glbDirectory}");
        }
        Debug.Log($"GLB directory: {glbDirectory}");
    }

    public async void ImportGLB(string fileName)
    {
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
