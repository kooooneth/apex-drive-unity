using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ApexDrive.Editor
{
    /// <summary>
    /// Builds the browser (WebGL) version. Run from the menu (Apex Drive > Build WebGL) or from the command line:
    /// Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod ApexDrive.Editor.WebGLBuilder.Build
    /// The game builds its whole world from code, so the scene is empty: Bootstrap creates everything after the scene loads.
    /// </summary>
    public static class WebGLBuilder
    {
        const string ScenePath = "Assets/Scenes/Boot.unity";
        const string OutDir = "Build/WebGL";

        [MenuItem("Apex Drive/Build WebGL")]
        public static void Build()
        {
            int code = 1;
            try { code = DoBuild() ? 0 : 1; }
            catch (System.Exception e) { Debug.LogError("WebGL build failed: " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool DoBuild()
        {
            // the scene is empty: the game builds its world from code after it loads (the colliders of CreatePrimitive are not used in the browser, see PrimitiveMeshes)
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // the game switches shader keywords on from code (emission, fade). A build only keeps the variants some material uses,
            // so one material of each kind is put in the scene; fog is switched on for the same reason
            Directory.CreateDirectory("Assets/WebGLKeep");
            var std = Shader.Find("Standard");
            var kept = new[] { MakeKeep(std, "Opaque", m => { }),
                               MakeKeep(std, "Emission", m => { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); }),
                               MakeKeep(std, "Fade", m => { m.SetFloat("_Mode", 3f); m.SetInt("_SrcBlend", 5); m.SetInt("_DstBlend", 10); m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000; }) };
            foreach (var m in kept)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.GetComponent<Renderer>().sharedMaterial = m;
                cube.transform.position = new Vector3(0f, -1000f, 0f);
                cube.SetActive(false);
            }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            var lightGo = new GameObject("Sun"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.shadows = LightShadows.Soft;
            EditorSceneManager.SaveScene(scene, ScenePath);

            // soft shadows and cascades must be switched on in the quality levels too, or those shader variants are stripped
            int level = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowCascades = 2; QualitySettings.shadowResolution = ShadowResolution.High;
            }
            QualitySettings.SetQualityLevel(level, false);

            // every material is made from code with Shader.Find, so the shaders have to be in the build
            IncludeShader("Standard");
            IncludeShader("Sprites/Default");
            IncludeShader("UI/Default");
            IncludeShader("Legacy Shaders/Particles/Alpha Blended");

            PlayerSettings.companyName = "kooooneth";
            PlayerSettings.productName = "Apex Drive";
            PlayerSettings.bundleVersion = "1.0." + System.DateTime.UtcNow.ToString("yyyyMMddHHmm");
            var target = NamedBuildTarget.WebGL;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Low);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;  // GitHub Pages does not send the encoding header for .br / .gz
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.initialMemorySize = 512;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.template = "PROJECT:Apex";

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("WebGL build result: " + report.summary.result + ", " + (report.summary.totalSize / (1024 * 1024)) + " MB, " + report.summary.totalTime);
            return report.summary.result == BuildResult.Succeeded;
        }

        static Material MakeKeep(Shader shader, string name, System.Action<Material> setup)
        {
            var m = new Material(shader) { name = name };
            setup(m);
            AssetDatabase.CreateAsset(m, "Assets/WebGLKeep/" + name + ".mat");
            return m;
        }

        static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) { Debug.LogWarning("Shader not found: " + name); return; }
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs.Length == 0) return;
            var so = new SerializedObject(gs[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("Always including shader " + name);
        }
    }
}
