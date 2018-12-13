using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

#region Bundler
public class PracticalBundler
{
    [ExecuteInEditMode]
    [MenuItem("Assets/Build Practical Bundle")]
    static void ExportResource()
    {
        string folder = EditorUtility.SaveFolderPanel("Bundle Folder", "", "");

        // Bring up save panel
        //string path = EditorUtility.SaveFilePanel("Save Resource", "", "Practical_" + Selection.activeGameObject.name, "unitypackage");
        if (folder.Length != 0)
        {
            Object[] selection = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
            Dictionary<string, Object> prefabbed = new Dictionary<string, Object>();
            List<string> guids = new List<string>();
            AssetPreview.SetPreviewTextureCacheSize(5000);
            string assetName = null;
            foreach (Object obj in selection)
            {
                // Generate GUID
                var guid = System.Guid.NewGuid();
                string guidStr = guid.ToString();

                string tempFolder = folder + "/" + guidStr;
                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }

                var cast = obj as GameObject;
                if (cast != null)
                {
                    GameObject original = GameObject.Instantiate(cast);
                    // Create Processed Prefab
                    GameObject mainParent = new GameObject();
                    mainParent.name = cast.name;
                    GameObject parent = new GameObject();
                    parent.transform.SetParent(mainParent.transform);
                    parent.name = cast.name;
                    GameObject go = GameObject.Instantiate(cast);
                    go.transform.SetParent(parent.transform);
                    go.transform.localPosition = Vector3.zero;
                    go.name = cast.name;

                    // Delete Scripts
                    MonoBehaviour[] allScripts = mainParent.GetComponentsInChildren<MonoBehaviour>();

                    foreach (MonoBehaviour oneScript in allScripts)
                    {
                        Object.DestroyImmediate(oneScript);
                    }

                    // Update Prefab
                    GameObject newfab = PrefabUtility.ReplacePrefab(mainParent, obj, ReplacePrefabOptions.ReplaceNameBased);
                    assetName = newfab.name;

                    // Save the bundle
                    string path = tempFolder + "/" + guidStr + "_" + newfab.name + ".unitypackage";

                    BuildPipeline.BuildAssetBundle(newfab, newfab.GetComponents(typeof(Component)), path,
                    BuildAssetBundleOptions.CollectDependencies | BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.CompleteAssets, BuildTarget.WSAPlayer);

                    // Grab Thumbnail
                    RuntimePreviewGenerator.TransparentBackground = true;
                    var preview = RuntimePreviewGenerator.GenerateModelPreview(mainParent.transform, 256, 256);
                    if (preview != null)
                    {
                        byte[] bytes = preview.EncodeToPNG();
                        Object.DestroyImmediate(preview);

                        // Path for png
                        UnityEngine.Windows.File.WriteAllBytes(tempFolder + "/" + guidStr + "_" + newfab.name + ".png", bytes);
                    }
                    // Update Prefab to Original
                    PrefabUtility.ReplacePrefab(original, newfab, ReplacePrefabOptions.ReplaceNameBased);

                    // Destroy old objects
                    GameObject.DestroyImmediate(go);
                    GameObject.DestroyImmediate(parent);
                    GameObject.DestroyImmediate(mainParent);
                    GameObject.DestroyImmediate(original);
                }

                DirectoryInfo directorySelected = new DirectoryInfo(tempFolder);
                PracticalCompression.Compress(directorySelected, assetName);
            }
            // Build the resource file from the active selection.
        }
    }
}


#endregion

#region Compression
public class PracticalCompression
{
    static public async Task<bool> WriteToFile(FileStream stream, string sDir, List<String> fileList)
    {
        List<byte> allBytes = new List<byte>();
        foreach (string sFilePath in fileList)
        {
            string[] sPathParts = sFilePath.Split('\\');
            if (sPathParts.Length <= 1)
            {
                sPathParts = sFilePath.Split('/');
            }
            char[] filePath = sPathParts[sPathParts.Length - 1].ToCharArray();

            string dataLength = filePath.Length.ToString();
            for (int i = 0; i < 32; i++)
            {
                byte c = (byte)((i < dataLength.Length) ? dataLength[i] : ' ');
                allBytes.Add(c);
            }

            foreach (char c in filePath)
            {
                byte b = (byte)c;
                allBytes.Add(b);
            }

            byte[] dataBytes = File.ReadAllBytes(Path.Combine(sDir, sFilePath));

            dataLength = dataBytes.Length.ToString();
            for (int i = 0; i < 32; i++)
            {
                byte c = (byte)((i < dataLength.Length) ? dataLength[i] : ' ');
                allBytes.Add(c);
            }

            allBytes.AddRange(dataBytes);

            await stream.WriteAsync(allBytes.ToArray(), 0, allBytes.Count);
            allBytes = new List<byte>();
        }

        return true;
        //}
    }

   static public async void Compress(DirectoryInfo folder, string name)
    {
        List<string> fileList = new List<string>();

        foreach (var file in folder.GetFiles())
        {
            fileList.Add(file.ToString());
        }
        var sDir = folder.ToString();
        var sMergedFile = folder.Parent + "/" + folder.Name + ".temp";
        var sCompressedFile = folder.Parent + "/" + folder.Name + "_" + name + ".pid";

        using (FileStream fileStream = new FileStream(sMergedFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Task mainTask = WriteToFile(fileStream, sDir, fileList);
            await mainTask.ContinueWith(_ =>
            {
                fileStream.Dispose();
                CompleteCompression(sCompressedFile, sDir, sMergedFile);
                foreach (var file in folder.GetFiles())
                {
                    file.Delete();
                }
                folder.Delete();
            });
        }
        
    }

    static async Task CompressFile(string sDir, string sRelativePath, GZipStream zipStream)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(sDir, sRelativePath));
        await zipStream.WriteAsync(bytes, 0, bytes.Length);
    }

    static public void WriteToFile(Stream stream, string sDir, List<String> fileList)
    {
        EditorApplication.QueuePlayerLoopUpdate();
        using (BinaryWriter bw = new BinaryWriter(stream))
        {
            foreach (string sFilePath in fileList)
            {
                string[] sPathParts = sFilePath.Split('\\');
                if (sPathParts.Length <= 1)
                {
                    sPathParts = sFilePath.Split('/');
                }
                char[] filePath = sPathParts[sPathParts.Length - 1].ToCharArray();

                string dataLength = filePath.Length.ToString();
                for (int i = 0; i < 32; i++)
                {
                    char c = (i < dataLength.Length) ? dataLength[i] : '\0';
                    bw.Write(BitConverter.GetBytes(c), 0, 1);
                }

                foreach (char c in filePath)
                    bw.Write(BitConverter.GetBytes(c), 0, 1);

                byte[] dataBytes = File.ReadAllBytes(Path.Combine(sDir, sFilePath));

                dataLength = dataBytes.Length.ToString();
                for (int i = 0; i < 32; i++)
                {
                    char c = (i < dataLength.Length) ? dataLength[i] : '\0';
                    bw.Write(BitConverter.GetBytes(c), 0, 1);
                }

                bw.Write(dataBytes, 0, dataBytes.Length);
            }
        }
    }
    static async void CompleteCompression(string sCompressedFile, string sDir, string sMergedFile)
    {
        using (FileStream outFile = new FileStream(sCompressedFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (GZipStream str = new GZipStream(outFile, CompressionMode.Compress))
            {
                Task task = CompressFile(sDir, sMergedFile, str);

                await task.ContinueWith(_ =>
                {
                    str.Dispose();
                    outFile.Dispose();
                    if (File.Exists(sMergedFile))
                    {
                        File.Delete(sMergedFile);
                    }
                });

            }
        }
    }
}
#endregion

#region Preview Creator
public static class RuntimePreviewGenerator
    {
        // Source: https://github.com/MattRix/UnityDecompiled/blob/master/UnityEngine/UnityEngine/Plane.cs
        private struct ProjectionPlane
        {
            private readonly Vector3 m_Normal;
            private readonly float m_Distance;

            public ProjectionPlane(Vector3 inNormal, Vector3 inPoint)
            {
                m_Normal = Vector3.Normalize(inNormal);
                m_Distance = -Vector3.Dot(inNormal, inPoint);
            }

            public Vector3 ClosestPointOnPlane(Vector3 point)
            {
                float d = Vector3.Dot(m_Normal, point) + m_Distance;
                return point - m_Normal * d;
            }

            public float GetDistanceToPoint(Vector3 point)
            {
                float signedDistance = Vector3.Dot(m_Normal, point) + m_Distance;
                if (signedDistance < 0f)
                    signedDistance = -signedDistance;

                return signedDistance;
            }
        }

        private class CameraSetup
        {
            private Vector3 position;
            private Quaternion rotation;

            private RenderTexture targetTexture;

            private Color backgroundColor;
            private bool orthographic;
            private float orthographicSize;
            private float nearClipPlane;
            private float farClipPlane;
            private float aspect;
            private CameraClearFlags clearFlags;

            public void GetSetup(Camera camera)
            {
                position = camera.transform.position;
                rotation = camera.transform.rotation;

                targetTexture = camera.targetTexture;

                backgroundColor = camera.backgroundColor;
                orthographic = camera.orthographic;
                orthographicSize = camera.orthographicSize;
                nearClipPlane = camera.nearClipPlane;
                farClipPlane = camera.farClipPlane;
                aspect = camera.aspect;
                clearFlags = camera.clearFlags;
            }

            public void ApplySetup(Camera camera)
            {
                camera.transform.position = position;
                camera.transform.rotation = rotation;

                camera.targetTexture = targetTexture;

                camera.backgroundColor = backgroundColor;
                camera.orthographic = orthographic;
                camera.orthographicSize = orthographicSize;
                camera.nearClipPlane = nearClipPlane;
                camera.farClipPlane = farClipPlane;
                camera.aspect = aspect;
                camera.clearFlags = clearFlags;

                targetTexture = null;
            }
        }

        private const int PREVIEW_LAYER = 22;
        private static Vector3 PREVIEW_POSITION = new Vector3(-9245f, 9899f, -9356f);

        private static Camera renderCamera;
        private static CameraSetup cameraSetup = new CameraSetup();

        private static List<Renderer> renderersList = new List<Renderer>(64);
        private static List<int> layersList = new List<int>(64);

        private static float aspect;
        private static float minX, maxX, minY, maxY;
        private static float maxDistance;

        private static Vector3 boundsCenter;
        private static ProjectionPlane projectionPlaneHorizontal, projectionPlaneVertical;

#if DEBUG_BOUNDS
	private static List<Transform> boundsDebugCubes = new List<Transform>( 8 );
	private static Material boundsMaterial;
#endif

        private static Camera m_internalCamera = null;
        private static Camera InternalCamera
        {
            get
            {
                if (m_internalCamera == null)
                {
                    m_internalCamera = new GameObject("ModelPreviewGeneratorCamera").AddComponent<Camera>();
                    m_internalCamera.enabled = false;
                    m_internalCamera.nearClipPlane = 0.01f;
                    m_internalCamera.cullingMask = 1 << PREVIEW_LAYER;
                    m_internalCamera.gameObject.hideFlags = HideFlags.HideAndDontSave;
                }

                return m_internalCamera;
            }
        }

        private static Camera m_previewRenderCamera;
        public static Camera PreviewRenderCamera
        {
            get { return m_previewRenderCamera; }
            set { m_previewRenderCamera = value; }
        }

        private static Vector3 m_previewDirection;
        public static Vector3 PreviewDirection
        {
            get { return m_previewDirection; }
            set { m_previewDirection = value.normalized; }
        }

        private static float m_padding;
        public static float Padding
        {
            get { return m_padding; }
            set { m_padding = Mathf.Clamp(value, -0.25f, 0.25f); }
        }

        private static Color m_backgroundColor;
        public static Color BackgroundColor
        {
            get { return m_backgroundColor; }
            set { m_backgroundColor = value; }
        }

        private static bool m_orthographicMode;
        public static bool OrthographicMode
        {
            get { return m_orthographicMode; }
            set { m_orthographicMode = value; }
        }

        private static bool m_transparentBackground;
        public static bool TransparentBackground
        {
            get { return m_transparentBackground; }
            set { m_transparentBackground = value; }
        }

        static RuntimePreviewGenerator()
        {
            PreviewRenderCamera = null;
            PreviewDirection = new Vector3(-1f, -1f, -1f);
            Padding = 0f;
            BackgroundColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            OrthographicMode = false;
            TransparentBackground = true;

#if DEBUG_BOUNDS
		boundsMaterial = new Material( Shader.Find( "Legacy Shaders/Diffuse" ) )
		{
			hideFlags = HideFlags.HideAndDontSave,
			color = new Color( 0f, 1f, 1f, 1f )
		};
#endif
        }

        public static Texture2D GenerateMaterialPreview(Material material, PrimitiveType previewObject, int width = 64, int height = 64)
        {
            return GenerateMaterialPreviewWithShader(material, previewObject, null, null, width, height);
        }

        public static Texture2D GenerateMaterialPreviewWithShader(Material material, PrimitiveType previewPrimitive, Shader shader, string replacementTag, int width = 64, int height = 64)
        {
            GameObject previewModel = GameObject.CreatePrimitive(previewPrimitive);
            previewModel.gameObject.hideFlags = HideFlags.HideAndDontSave;
            previewModel.GetComponent<Renderer>().sharedMaterial = material;

            try
            {
                return GenerateModelPreviewWithShader(previewModel.transform, shader, replacementTag, width, height, false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                Object.DestroyImmediate(previewModel);
            }

            return null;
        }

        public static Texture2D GenerateModelPreview(Transform model, int width = 64, int height = 64, bool shouldCloneModel = false)
        {
            return GenerateModelPreviewWithShader(model, null, null, width, height, shouldCloneModel);
        }

        public static Texture2D GenerateModelPreviewWithShader(Transform model, Shader shader, string replacementTag, int width = 64, int height = 64, bool shouldCloneModel = false)
        {
            if (model == null || model.Equals(null))
                return null;

            Texture2D result = null;

            if (!model.gameObject.scene.IsValid() || !model.gameObject.scene.isLoaded)
                shouldCloneModel = true;

            Transform previewObject;
            if (shouldCloneModel)
            {
                previewObject = (Transform)Object.Instantiate(model, null, false);
                previewObject.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                previewObject = model;

                layersList.Clear();
                GetLayerRecursively(previewObject);
            }

            bool isStatic = IsStatic(model);
            bool wasActive = previewObject.gameObject.activeSelf;
            Vector3 prevPos = previewObject.position;
            Quaternion prevRot = previewObject.rotation;

            try
            {
                SetupCamera();
                SetLayerRecursively(previewObject);

                if (!isStatic)
                {
                    previewObject.position = PREVIEW_POSITION;
                    previewObject.rotation = Quaternion.identity;
                }

                if (!wasActive)
                    previewObject.gameObject.SetActive(true);

                Vector3 previewDir = previewObject.rotation * m_previewDirection;

                renderersList.Clear();
                previewObject.GetComponentsInChildren(renderersList);

                Bounds previewBounds = new Bounds();
                bool init = false;
                for (int i = 0; i < renderersList.Count; i++)
                {
                    if (!renderersList[i].enabled)
                        continue;

                    if (!init)
                    {
                        previewBounds = renderersList[i].bounds;
                        init = true;
                    }
                    else
                        previewBounds.Encapsulate(renderersList[i].bounds);
                }

                if (!init)
                    return null;

                boundsCenter = previewBounds.center;
                Vector3 boundsExtents = previewBounds.extents;
                Vector3 boundsSize = 2f * boundsExtents;

                aspect = (float)width / height;
                renderCamera.aspect = aspect;
                renderCamera.transform.rotation = Quaternion.LookRotation(previewDir, previewObject.up);

#if DEBUG_BOUNDS
			boundsDebugCubes.Clear();
#endif

                float distance;
                if (m_orthographicMode)
                {
                    renderCamera.transform.position = boundsCenter;

                    minX = minY = Mathf.Infinity;
                    maxX = maxY = Mathf.NegativeInfinity;

                    Vector3 point = boundsCenter + boundsExtents;
                    ProjectBoundingBoxMinMax(point);
                    point.x -= boundsSize.x;
                    ProjectBoundingBoxMinMax(point);
                    point.y -= boundsSize.y;
                    ProjectBoundingBoxMinMax(point);
                    point.x += boundsSize.x;
                    ProjectBoundingBoxMinMax(point);
                    point.z -= boundsSize.z;
                    ProjectBoundingBoxMinMax(point);
                    point.x -= boundsSize.x;
                    ProjectBoundingBoxMinMax(point);
                    point.y += boundsSize.y;
                    ProjectBoundingBoxMinMax(point);
                    point.x += boundsSize.x;
                    ProjectBoundingBoxMinMax(point);

                    distance = boundsExtents.magnitude + 1f;
                    renderCamera.orthographicSize = (1f + m_padding * 2f) * Mathf.Max(maxY - minY, (maxX - minX) / aspect) * 0.5f;
                }
                else
                {
                    projectionPlaneHorizontal = new ProjectionPlane(renderCamera.transform.up, boundsCenter);
                    projectionPlaneVertical = new ProjectionPlane(renderCamera.transform.right, boundsCenter);

                    maxDistance = Mathf.NegativeInfinity;

                    Vector3 point = boundsCenter + boundsExtents;
                    CalculateMaxDistance(point);
                    point.x -= boundsSize.x;
                    CalculateMaxDistance(point);
                    point.y -= boundsSize.y;
                    CalculateMaxDistance(point);
                    point.x += boundsSize.x;
                    CalculateMaxDistance(point);
                    point.z -= boundsSize.z;
                    CalculateMaxDistance(point);
                    point.x -= boundsSize.x;
                    CalculateMaxDistance(point);
                    point.y += boundsSize.y;
                    CalculateMaxDistance(point);
                    point.x += boundsSize.x;
                    CalculateMaxDistance(point);

                    distance = (1f + m_padding * 2f) * Mathf.Sqrt(maxDistance);
                }

                renderCamera.transform.position = boundsCenter - previewDir * distance;
                renderCamera.farClipPlane = distance * 4f;

                RenderTexture temp = RenderTexture.active;
                RenderTexture renderTex = RenderTexture.GetTemporary(width, height, 16);
                RenderTexture.active = renderTex;
                if (m_transparentBackground)
                    GL.Clear(false, true, Color.clear);

                renderCamera.targetTexture = renderTex;

                if (shader == null)
                    renderCamera.Render();
                else
                    renderCamera.RenderWithShader(shader, replacementTag == null ? string.Empty : replacementTag);

                renderCamera.targetTexture = null;

                result = new Texture2D(width, height, m_transparentBackground ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                result.Apply(false, false);

                RenderTexture.active = temp;
                RenderTexture.ReleaseTemporary(renderTex);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
#if DEBUG_BOUNDS
			for( int i = 0; i < boundsDebugCubes.Count; i++ )
				Object.DestroyImmediate( boundsDebugCubes[i].gameObject );

			boundsDebugCubes.Clear();
#endif

                if (shouldCloneModel)
                    Object.DestroyImmediate(previewObject.gameObject);
                else
                {
                    if (!wasActive)
                        previewObject.gameObject.SetActive(false);

                    if (!isStatic)
                    {
                        previewObject.position = prevPos;
                        previewObject.rotation = prevRot;
                    }

                    int index = 0;
                    SetLayerRecursively(previewObject, ref index);
                }

                if (renderCamera == m_previewRenderCamera)
                    cameraSetup.ApplySetup(renderCamera);
            }

            return result;
        }

        private static void SetupCamera()
        {
            if (m_previewRenderCamera != null && !m_previewRenderCamera.Equals(null))
            {
                cameraSetup.GetSetup(m_previewRenderCamera);

                renderCamera = m_previewRenderCamera;
                renderCamera.nearClipPlane = 0.01f;
            }
            else
                renderCamera = InternalCamera;

            renderCamera.backgroundColor = m_backgroundColor;
            renderCamera.orthographic = m_orthographicMode;
            renderCamera.clearFlags = m_transparentBackground ? CameraClearFlags.Depth : CameraClearFlags.Color;
        }

        private static void ProjectBoundingBoxMinMax(Vector3 point)
        {
#if DEBUG_BOUNDS
		CreateDebugCube( point, Vector3.zero, new Vector3( 0.5f, 0.5f, 0.5f ) );
#endif

            Vector3 localPoint = renderCamera.transform.InverseTransformPoint(point);
            if (localPoint.x < minX)
                minX = localPoint.x;
            if (localPoint.x > maxX)
                maxX = localPoint.x;
            if (localPoint.y < minY)
                minY = localPoint.y;
            if (localPoint.y > maxY)
                maxY = localPoint.y;
        }

        private static void CalculateMaxDistance(Vector3 point)
        {
#if DEBUG_BOUNDS
		CreateDebugCube( point, Vector3.zero, new Vector3( 0.5f, 0.5f, 0.5f ) );
#endif

            Vector3 intersectionPoint = projectionPlaneHorizontal.ClosestPointOnPlane(point);

            float horizontalDistance = projectionPlaneHorizontal.GetDistanceToPoint(point);
            float verticalDistance = projectionPlaneVertical.GetDistanceToPoint(point);

            // Credit: https://docs.unity3d.com/Manual/FrustumSizeAtDistance.html
            float halfFrustumHeight = Mathf.Max(verticalDistance, horizontalDistance / aspect);
            float distance = halfFrustumHeight / Mathf.Tan(renderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float distanceToCenter = (intersectionPoint - m_previewDirection * distance - boundsCenter).sqrMagnitude;
            if (distanceToCenter > maxDistance)
                maxDistance = distanceToCenter;
        }

        private static bool IsStatic(Transform obj)
        {
            if (obj.gameObject.isStatic)
                return true;

            for (int i = 0; i < obj.childCount; i++)
            {
                if (IsStatic(obj.GetChild(i)))
                    return true;
            }

            return false;
        }

        private static void SetLayerRecursively(Transform obj)
        {
            obj.gameObject.layer = PREVIEW_LAYER;
            for (int i = 0; i < obj.childCount; i++)
                SetLayerRecursively(obj.GetChild(i));
        }

        private static void GetLayerRecursively(Transform obj)
        {
            layersList.Add(obj.gameObject.layer);
            for (int i = 0; i < obj.childCount; i++)
                GetLayerRecursively(obj.GetChild(i));
        }

        private static void SetLayerRecursively(Transform obj, ref int index)
        {
            obj.gameObject.layer = layersList[index++];
            for (int i = 0; i < obj.childCount; i++)
                SetLayerRecursively(obj.GetChild(i), ref index);
        }

#if DEBUG_BOUNDS
	private static void CreateDebugCube( Vector3 position, Vector3 rotation, Vector3 scale )
	{
		Transform cube = GameObject.CreatePrimitive( PrimitiveType.Cube ).transform;
		cube.localPosition = position;
		cube.localEulerAngles = rotation;
		cube.localScale = scale;
		cube.gameObject.layer = PREVIEW_LAYER;
		cube.gameObject.hideFlags = HideFlags.HideAndDontSave;

		cube.GetComponent<Renderer>().sharedMaterial = boundsMaterial;

		boundsDebugCubes.Add( cube );
	}
#endif
    }
#endregion

#region Helpers
public class EditorAsyncPump
{
    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.update += ExecuteContinuations;
    }
    private static void ExecuteContinuations()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // Not in Edit mode, don't interfere
            return;
        }
        var context = System.Threading.SynchronizationContext.Current;
        if (_execMethod == null)
        {
            _execMethod = context.GetType().GetMethod("Exec", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        _execMethod.Invoke(context, null);
    }
    private static MethodInfo _execMethod;
}
#endregion