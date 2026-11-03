using Capsule.Core;
using System.Windows.Media.Imaging;

namespace Capsule.App;

public partial class MainWindow
{
    private readonly CoverClient coverClient;
    private readonly record struct CoverIdentity(string Source,string Title,string Artist,string Album,long Duration,bool Video,bool Online);
    private readonly Dictionary<CoverIdentity,BitmapSource> networkCovers=[];
    private readonly Dictionary<string,BitmapSource> playerIcons=[];
    private CancellationTokenSource? coverLookup;
    private BitmapSource? networkCover,playerIcon;
    private CoverIdentity? networkCoverKey;
    internal BitmapSource? EffectiveCover=>!settings.MediaEnabled || !state.Media.Available || SoloSelected ? null : (settings.OnlineCoversEnabled ? networkCover : null) ?? (state.Media.IsVideo && state.Media.SourceId.Contains("potplayer",StringComparison.OrdinalIgnoreCase) ? playerIcon : media.Cover ?? playerIcon);
    private void InitializeCovers()=>Closed+=(_,_)=>{coverLookup?.Cancel();coverLookup?.Dispose();coverClient.Dispose();networkCovers.Clear();playerIcons.Clear();};
    private void UpdateCovers(MediaSnapshot track)
    {
        CoverIdentity? key=settings.MediaEnabled && track.Available && !SoloSelected ? new(track.SourceId,track.Title,track.Artist,track.Album,track.Duration.Ticks,track.IsVideo,settings.OnlineCoversEnabled) : null;
        if(key==networkCoverKey)return;
        networkCoverKey=key;coverLookup?.Cancel();coverLookup?.Dispose();coverLookup=null;networkCover=playerIcon=null;
        if(key==null)return;
        networkCover=networkCovers.GetValueOrDefault(key.Value);playerIcon=playerIcons.GetValueOrDefault(track.SourceId);
        coverLookup=new();_=LookupCoversAsync(track,key.Value,coverLookup.Token);
    }
    private async Task LookupCoversAsync(MediaSnapshot track,CoverIdentity key,CancellationToken token)
    {
        try
        {
            if(playerIcon==null)
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var icon=await ApplicationIcons.ReadAsync(track.SourceId,timeout.Token).WaitAsync(timeout.Token);
                    if(!closing && !token.IsCancellationRequested && key==networkCoverKey && icon!=null){playerIcon=icon;if(playerIcons.Count>=32)playerIcons.Remove(playerIcons.Keys.First());playerIcons[track.SourceId]=icon;Render();}
                }
                catch(OperationCanceledException) when(!token.IsCancellationRequested){ }
            }
            if(!settings.OnlineCoversEnabled || networkCover!=null || track.IsVideo || string.IsNullOrWhiteSpace(track.Artist))return;
            for(var attempt=0;attempt<2;attempt++)
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(12));BitmapSource? cover=null;
                try{cover=await coverClient.FetchAsync(track,timeout.Token);}catch(OperationCanceledException) when(!token.IsCancellationRequested){ }
                if(closing || token.IsCancellationRequested || key!=networkCoverKey)return;
                if(cover!=null)
                {
                    networkCover=cover;if(networkCovers.Count>=16)networkCovers.Remove(networkCovers.Keys.First());networkCovers[key]=cover;Render();return;
                }
                if(attempt==0)await Task.Delay(10000,token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){ }
        catch(Exception){ /* A missing cover must not interrupt metadata or controls. */ }
    }
}
