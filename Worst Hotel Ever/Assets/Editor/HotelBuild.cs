using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorstHotel.BuildTools
{
    public static class HotelBuild
    {
        public static string Root=>Directory.GetParent(Application.dataPath).Parent.FullName;
        public static void Validate()
        {
            Directory.CreateDirectory(Path.Combine(Root,"TestResults"));
            try {
                var results=HotelSimulationTests.RunAll();
                File.WriteAllLines(Path.Combine(Root,"TestResults","simulation-tests.txt"),results);
                Debug.Log("WHE_TESTS_PASSED count="+results.Count);
            } catch(Exception e) {File.WriteAllText(Path.Combine(Root,"TestResults","simulation-tests-failed.txt"),e.ToString());throw;}
        }
        [MenuItem("Worst Hotel/Build Windows pre-MVP")]
        public static void BuildWindows()
        {
            Validate();
            EditorSettings.serializationMode=SerializationMode.ForceText;
            PlayerSettings.companyName="Almost Grand";PlayerSettings.productName="Worst Hotel Ever";
            PlayerSettings.bundleVersion="0.1.0";
            PlayerSettings.runInBackground=true;PlayerSettings.resizableWindow=true;
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone,ManagedStrippingLevel.Low);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Directory.CreateDirectory(Path.Combine(Application.dataPath,"Scenes"));
            const string scenePath="Assets/Scenes/Hotel.unity";
            EditorSceneManager.SaveScene(scene,scenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scenePath,true)};
            AssetDatabase.SaveAssets();
            string folder=Path.Combine(Root,"Builds","Windows");Directory.CreateDirectory(folder);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{scenePath},locationPathName=Path.Combine(folder,"WorstHotelEver.exe"),
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development
            });
            string result="Result: "+report.summary.result+"\nVersion: 0.1.0\nUnity: "+Application.unityVersion+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nBytes: "+report.summary.totalSize+"\nDuration: "+report.summary.totalTime;
            File.WriteAllText(Path.Combine(Root,"TestResults","build-summary.txt"),result);
            Debug.Log("WHE_BUILD "+result);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed");
            string licenses=Path.Combine(Application.dataPath,"Steam","Licenses");
            if(Directory.Exists(licenses))foreach(string file in Directory.GetFiles(licenses,"*",SearchOption.AllDirectories)) {
                if(file.EndsWith(".meta"))continue;
                string target=Path.Combine(folder,"ThirdPartyNotices",Path.GetRelativePath(licenses,file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
            }
            string readme=Path.Combine(Root,"README_PRE_MVP.md");
            if(File.Exists(readme))File.Copy(readme,Path.Combine(folder,"READ_ME_RU.md"),true);
        }
    }
}
