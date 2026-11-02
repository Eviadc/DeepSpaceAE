using System;
using System.IO;
using UnityEngine;

namespace DeepSpaceAE.RuntimeTests;

public sealed class Plugin : IMod
{
    internal const string Id="deepspaceae.runtime-tests",Scenario="deepspaceae.runtime-pilot";
    private RuntimePilot? runner;
    public void Init(ModManifest manifest) { }
    public void OnEnable()
    {
        ModHook.OnNewGameStarted += Started;
        ModHelper.AddStartingScenario(Id,Scenario,()=>"AE Runtime Pilot (test save)",()=>"Isolated one-shot runtime test. Not a normal playthrough.",
            Array.Empty<Func<string>>(),()=>RuntimePilot.FreshScenarioStarted=true,skipIntro:true);
        string configPath=Path.Combine(ModLoader.ModsPath,"DeepSpaceAE.RuntimeTests","run.json");
        if(!File.Exists(configPath)) return;
        var config=JsonUtility.FromJson<PilotConfig>(File.ReadAllText(configPath));
        if(config==null || !Guid.TryParseExact(config.runId,"N",out _) || !Path.IsPathRooted(config.outputDirectory))
            throw new InvalidOperationException("Invalid explicit runtime pilot configuration.");
        string output=Path.GetFullPath(config.outputDirectory);
        if(!System.IO.Directory.Exists(output) || File.Exists(Path.Combine(output,"result.json")))
            throw new InvalidOperationException("Pilot output must be a fresh precreated run directory.");
        // Consume the arm file before any game/save action. A crash cannot auto-replay.
        File.Move(configPath,Path.Combine(Path.GetDirectoryName(configPath)!,"run."+config.runId+".claimed.json"));
        var host=new GameObject("DeepSpaceAE isolated runtime pilot");
        UnityEngine.Object.DontDestroyOnLoad(host);
        runner=host.AddComponent<RuntimePilot>(); runner.Configure(config);
    }
    public void OnDisable()
    {
        ModHook.OnNewGameStarted -= Started;
        ModHelper.RemoveStartingScenarios(Id);
        if(runner!=null) UnityEngine.Object.Destroy(runner.gameObject);
    }
    private void Started(string scenario)
    { if (runner != null && scenario == runner.ExpectedScenario) RuntimePilot.FreshScenarioStarted = true; }
}

[Serializable]
public sealed class PilotConfig
{
    public string runId="",outputDirectory="",productCommit="";
    public int[] existingSlots=Array.Empty<int>();
    public bool exitAfterRun=true;
    public bool engineerStart;
}
[Serializable]
public sealed class CaseResult
{
    public string id="",status="",detail="";
    public double elapsedMs;
}
[Serializable]
public sealed class PilotReport
{
    public string runId="",status="waiting_for_menu",productCommit="",startedUtc="",finishedUtc="",unityVersion="",error="";
    public int saveSlot=-1,day=-1;
    public bool freshScenarioConfirmed,mouseInputTested=false,saveReloadTested=false;
    public string profile="";
    public string activeScene="",nativeScenario="";
    public bool hasGameRoot,hasPlayerStore,scenarioCallbackSeen,sessionLoading=true;
    public int nativeSaveSlot=-1;
    public bool startupHintInitiallyVisible,startupHintClosed;
    public string startupHintHandledBy="";
    [NonSerialized] public System.Collections.Generic.List<CaseResult> cases=new();
}
