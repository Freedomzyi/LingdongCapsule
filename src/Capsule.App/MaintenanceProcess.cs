using System.Diagnostics;
using System.IO;

namespace Capsule.App;

internal sealed record MaintenanceProgress(string Stage,int Completed,int Total)
{
    internal static MaintenanceProgress? Parse(string line)
    {
        var parts=line.Split('|');
        if(parts.Length!=4 || parts[0]!="CAPSULE_PROGRESS" || !int.TryParse(parts[2],out var done) || !int.TryParse(parts[3],out var total) || done<0 || total<0 || done>total)return null;
        return new(parts[1],done,total);
    }
}

internal static class MaintenanceProcess
{
    internal static async Task RunAsync(string script,IEnumerable<string> arguments,IProgress<MaintenanceProgress> progress)
    {
        if(!File.Exists(script))throw new IOException("安装文件不完整，请重新下载并完整解压。");
        var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script}.Concat(arguments))start.ArgumentList.Add(argument);
        using var process=Process.Start(start) ?? throw new IOException("无法启动维护服务，请稍后重试。");
        var errorTask=process.StandardError.ReadToEndAsync();
        while(await process.StandardOutput.ReadLineAsync() is {} line)
            if(MaintenanceProgress.Parse(line) is {} update)progress.Report(update);
        await process.WaitForExitAsync();var error=await errorTask;
        if(process.ExitCode!=0)throw new IOException(string.IsNullOrWhiteSpace(error) ? "操作未完成，请检查安装目录权限后重试。" : error.Length>3000 ? error[..3000] : error);
    }
}
