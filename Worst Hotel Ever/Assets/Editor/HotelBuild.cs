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
                var results=new System.Collections.Generic.List<string>();
                var failures=new System.Collections.Generic.List<Exception>();
                void Run(string name,Func<System.Collections.Generic.List<string>> suite) {
                    try { var part=suite();results.AddRange(part);Debug.Log("WHE_SUITE_PASS "+name+" count="+part.Count); }
                    catch(Exception error){failures.Add(new Exception(name,error));Debug.Log("WHE_SUITE_FAIL "+name+": "+error);}
                }
                Run("LegacySimulation",HotelSimulationTests.RunAll);
                Run("LegacyPresentation",HotelPresentationTests.RunAll);
                Run("LegacyOnboarding",HotelOnboardingTests.RunAll);
                Run("LegacyDirector",HotelDirectorTests.RunAll);
                Run("Feedback",HotelFeedbackTests.RunAll);
                Run("Figures",HotelFigureTests.RunAll);
                Run("MvpLifecycle",HotelMvpLifecycleTests.RunAll);
                Run("SnapshotCodec",HotelSnapshotCodecTests.RunAll);
                Run("MvpOperations",HotelMvpOperationsTests.RunAll);
                Run("MvpHospitality",HotelMvpHospitalityTests.RunAll);
                Run("MvpDirector",HotelMvpDirectorTests.RunAll);
                Run("MvpPersistence",HotelMvpPersistenceTests.RunAll);
                Run("MvpPresentation",HotelMvpPresentationTests.RunAll);
                Run("MvpWorld",HotelMvpWorldTests.RunAll);
                Run("MvpLongRun",HotelMvpLongRunTests.RunAll);
                Run("DangerGameplay",HotelDangerTests.RunAll);
                Run("DangerPersistence",HotelDangerPersistenceTests.RunAll);
                Run("DangerComplaints",HotelDangerComplaintTests.RunAll);
                Run("DangerPresentation",HotelDangerPresentationTests.RunAll);
                Run("DangerWorld",HotelDangerWorldTests.RunAll);
                Run("CompactHud",HotelHudTests.RunAll);
                Run("TabletPresentation",HotelTabletTests.RunAll);
                Run("Stamina",HotelStaminaTests.RunAll);
                Run("GrimTextures",HotelTextureTests.RunAll);
                File.WriteAllLines(Path.Combine(Root,"TestResults","simulation-tests.txt"),results);
                if(failures.Count!=0)throw new AggregateException("Native suites failed: "+failures.Count,failures);
                Debug.Log("WHE_TESTS_PASSED count="+results.Count);
            } catch(Exception e) {File.WriteAllText(Path.Combine(Root,"TestResults","simulation-tests-failed.txt"),e.ToString());throw;}
        }
        [MenuItem("Worst Hotel/Build Windows danger alpha")]
        public static void BuildWindows()
        {
            BuildWindowsAt("Windows");
        }
        public static void BuildWindowsUi()
        {
            BuildWindowsAt("WindowsUI");
        }
        public static void BuildWindowsTablet(){BuildWindowsAt("WindowsTablet");}
        public static void BuildWindowsGrim(){BuildWindowsAt("WindowsGrim");}
        static void BuildWindowsAt(string outputDirectory)
        {
            Validate();
            EditorSettings.serializationMode=SerializationMode.ForceText;
            PlayerSettings.companyName="Almost Grand";PlayerSettings.productName="Worst Hotel Ever";
            PlayerSettings.bundleVersion="1.3.0";
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
            string folder=Path.Combine(Root,"Builds",outputDirectory);Directory.CreateDirectory(folder);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{scenePath},locationPathName=Path.Combine(folder,"WorstHotelEver.exe"),
                target=BuildTarget.StandaloneWindows64,options=outputDirectory=="WindowsTablet"||outputDirectory=="WindowsGrim"?BuildOptions.None:BuildOptions.Development
            });
            string result="Result: "+report.summary.result+"\nVersion: "+PlayerSettings.bundleVersion+"\nUnity: "+Application.unityVersion+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nBytes: "+report.summary.totalSize+"\nDuration: "+report.summary.totalTime;
            File.WriteAllText(Path.Combine(Root,"TestResults","build-summary.txt"),result);
            Debug.Log("WHE_BUILD "+result);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed");
            string licenses=Path.Combine(Application.dataPath,"Steam","Licenses");
            if(Directory.Exists(licenses))foreach(string file in Directory.GetFiles(licenses,"*",SearchOption.AllDirectories)) {
                if(file.EndsWith(".meta"))continue;
                string target=Path.Combine(folder,"ThirdPartyNotices",Path.GetRelativePath(licenses,file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
            }
            string readme=Path.Combine(Root,"README_DANGER.md");
            if(File.Exists(readme))File.Copy(readme,Path.Combine(folder,"READ_ME_RU.md"),true);
            string status=Path.Combine(Root,"docs","IMPLEMENTATION_STATUS.md");
            if(File.Exists(status)){Directory.CreateDirectory(Path.Combine(folder,"docs"));File.Copy(status,Path.Combine(folder,"docs","IMPLEMENTATION_STATUS.md"),true);}
            string launcher=Path.Combine(Root,"Tools","PLAYTEST_MVP.cmd");
            if(File.Exists(launcher))File.Copy(launcher,Path.Combine(folder,"PLAYTEST_MVP.cmd"),true);
            string dangerLauncher=Path.Combine(Root,"Tools","PLAYTEST_DANGER.cmd");
            if(File.Exists(dangerLauncher))File.Copy(dangerLauncher,Path.Combine(folder,"PLAYTEST_DANGER.cmd"),true);
            string dangerGuide=Path.Combine(Root,"docs","DANGER_PLAYTEST_RU.md");
            if(File.Exists(dangerGuide))File.Copy(dangerGuide,Path.Combine(folder,"docs","DANGER_PLAYTEST_RU.md"),true);
            string playtest=Path.Combine(Root,"docs","MVP_TWO_PC_PLAYTEST_RU.md");
            if(File.Exists(playtest)){Directory.CreateDirectory(Path.Combine(folder,"docs"));File.Copy(playtest,Path.Combine(folder,"docs","MVP_TWO_PC_PLAYTEST_RU.md"),true);}
            string collector=Path.Combine(Root,"Tools","Collect-PlaytestDiagnostics.ps1");
            if(File.Exists(collector)){Directory.CreateDirectory(Path.Combine(folder,"Tools"));File.Copy(collector,Path.Combine(folder,"Tools","Collect-PlaytestDiagnostics.ps1"),true);}
        }
    }
}
