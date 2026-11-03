using Capsule.Core;
using System.IO;

namespace Capsule.App;

public partial class MainWindow
{
    private MspBridge msp=null!;
    private string mspDirectory="";
    private void InitializeMsp()
    {
        msp=new MspBridge(
            notice=>Dispatcher.InvokeAsync(()=>!closing && AcceptNotification(notice,DateTimeOffset.Now)).Task.WaitAsync(TimeSpan.FromSeconds(3)),
            ()=>Dispatcher.InvokeAsync(()=>state.Media).Task.WaitAsync(TimeSpan.FromSeconds(3)),
            action=>Dispatcher.InvokeAsync(async()=>
            {
                if(closing || !settings.MediaEnabled)return false;
                await media.ReadAsync();return await media.ControlAsync(action);
            }).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(8)));
        Closed+=(_,_)=>{closing=true;msp.Dispose();};
    }
    private void ApplyMsp()
    {
        if(!settings.MspEnabled){if(msp.Enabled)msp.Dispose();return;}
        if(!msp.Start(mspDirectory))settings.MspEnabled=false;
    }
}
