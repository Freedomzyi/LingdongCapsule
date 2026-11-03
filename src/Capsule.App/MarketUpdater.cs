using System.IO;

namespace Capsule.App;

internal sealed record MarketBatchResult(int Succeeded,IReadOnlyDictionary<string,string> Failures,bool Canceled);
internal sealed class MarketUpdater(PluginMarket market)
{
    internal async Task<MarketBatchResult> RunAsync(IEnumerable<MarketPlugin> packages,string folder,Version host,Func<string,string,bool> install,Action<string> progress,CancellationToken token)
    {
        var succeeded=0;var failures=new Dictionary<string,string>();var canceled=false;
        foreach(var package in packages)
        {
            if(token.IsCancellationRequested){canceled=true;break;}
            string? file=null;
            try
            {
                progress(package.Name);file=await market.DownloadAsync(package,folder,host,token);token.ThrowIfCancellationRequested();
                install(file,package.Id);succeeded++;
            }
            catch(OperationCanceledException){canceled=true;break;}
            catch(Exception error){failures[package.Id]=error.Message;}
            finally
            {
                if(file!=null)try{File.Delete(file);}catch(Exception error) when(error is IOException or UnauthorizedAccessException){failures[package.Id]="组件操作已结束，但下载临时文件未能清理。";}
            }
        }
        return new(succeeded,failures,canceled);
    }
}
