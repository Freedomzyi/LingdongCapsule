using System.IO;
using System.Text.Json;
using Capsule.App;
using Capsule.Core;

internal static class DiagnosticsTests
{
    internal static async Task RunAsync()
    {
        if(AppDiagnostics.FromArguments([])!=null)throw new Exception("Diagnostics enabled without explicit flag");
        foreach(var args in new[]{new[]{"--diagnostics-dir"},new[]{"--diagnostics-dir","relative"},new[]{"--diagnostics-dir",@"\\invalid\share"},new[]{"--diagnostics-dir","x","--diagnostics-dir","y"}})
        {var rejected=false;try{AppDiagnostics.FromArguments(args);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid diagnostic CLI accepted");}
        var root=Path.GetFullPath(Path.Combine("artifacts","diagnostic-tests",Guid.NewGuid().ToString("N")));
        var diagnostic=new AppDiagnostics(root);
        try
        {
            diagnostic.Observe(IslandMode.Clock,false,false,0);await Task.Delay(40);diagnostic.Observe(IslandMode.Media,false,true,1);await Task.Delay(40);
            diagnostic.Observe(IslandMode.Notification,false,false,1);await Task.Delay(40);diagnostic.Observe(IslandMode.Clock,true,false,0);await Task.Delay(40);diagnostic.Observe(IslandMode.Clock,false,false,0);
            diagnostic.Observe(IslandMode.Media,false,true,1,fixtureMedia:true);await Task.Delay(40);diagnostic.Observe(IslandMode.Clock,false,false,0);
            // Explicit method fixtures, not an actual machine sleep/resume acceptance.
            diagnostic.Power("Suspend");diagnostic.Power("Resume");diagnostic.Unhandled(new IOException("PRIVATE_TITLE_TOKEN_MUST_NOT_BE_RECORDED"));
            var samples=Path.Combine(diagnostic.DirectoryPath,"samples.jsonl");
            using(var held=new FileStream(samples,FileMode.Open,FileAccess.Read,FileShare.None))diagnostic.Sample();
            diagnostic.Sample();diagnostic.Finish(7);
            var before=File.ReadAllText(samples);diagnostic.Observe(IslandMode.Media,false,true,9);diagnostic.Power("Resume");diagnostic.Sample();
            if(File.ReadAllText(samples)!=before)throw new Exception("Disposed diagnostics kept writing");
            using var sample=JsonDocument.Parse(File.ReadAllLines(samples)[^1]);var value=sample.RootElement;
            if(value.GetProperty("writeErrors").GetInt32()<1 || value.GetProperty("resumeCount").GetInt32()!=1 || value.GetProperty("unhandledCount").GetInt32()!=1)throw new Exception("Diagnostic counters or IO recovery failed");
            foreach(var scene in new[]{"idle","media-playing","notification","hidden","fixture-media"})if(value.GetProperty("sceneSeconds").GetProperty(scene).GetDouble()<=0)throw new Exception("Scene elapsed time missing");
            using var exit=JsonDocument.Parse(File.ReadAllLines(Path.Combine(diagnostic.DirectoryPath,"exit.jsonl")).Single());if(exit.RootElement.GetProperty("exitCode").GetInt32()!=7)throw new Exception("Exit result missing");
            foreach(var file in Directory.GetFiles(diagnostic.DirectoryPath))if(File.ReadAllText(file).Contains("PRIVATE_TITLE_TOKEN_MUST_NOT_BE_RECORDED"))throw new Exception("Diagnostic log retained private exception message");
        }
        finally{diagnostic.Dispose();}
        Console.WriteLine("PASS explicit-only diagnostic CLI, aggregate scene timing, IO refusal/recovery, type-only errors, exit and disposed cleanup; simulated power callbacks are not sleep acceptance; evidence: "+root);
    }
}
