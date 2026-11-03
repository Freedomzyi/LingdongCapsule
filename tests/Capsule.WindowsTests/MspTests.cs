using Capsule.App;
using Capsule.Core;
using Msp.Core;
using System.IO;
using System.Text.Json;

internal static class MspTests
{
    internal static async Task RunAsync()
    {
        var directory=Path.Combine(Environment.CurrentDirectory,"artifacts","msp-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        Notice? received=null;string? command=null;
        using var bridge=new MspBridge(notice=>{received=notice;return Task.FromResult(true);},()=>Task.FromResult(new MediaSnapshot("Test track","Test artist",true,true)),action=>{command=action;return Task.FromResult(true);});
        if(!bridge.Start(directory))throw new Exception(bridge.Error);
        var client=new MspNode("capsule-test-peer",nodesDir:directory);
        try
        {
            client.DeclareSignal("notify.capsule.fixture");
            client.Start();string? ping=null;
            for(var attempt=0;attempt<30;attempt++)
            {
                try{ping=JsonSerializer.Serialize((await client.CallAsync("lingdongcapsule","ping",new{})).Unwrap());if(ping.Contains("lingdongcapsule"))break;}catch(Exception){ }
                await Task.Delay(200);
            }
            if(ping==null || !ping.Contains("lingdongcapsule"))throw new Exception("MSP peers did not exchange ping");
            await client.CallAsync("lingdongcapsule","notify",new{title="MSP test",body="Independent local fixture"});
            if(received?.Title!="MSP test")throw new Exception("MSP notification did not reach adapter");
            client.Publish("notify.capsule.fixture",new{title="Subscribed fixture",body="Broadcast test only"});
            for(var attempt=0;attempt<30 && received?.Title!="Subscribed fixture";attempt++)await Task.Delay(100);
            if(received?.Title!="Subscribed fixture" || received.SourceId!="msp:capsule-test-peer")throw new Exception("MSP subscription source identity was lost");
            await client.CallAsync("lingdongcapsule","media_control",new{action="prev"});if(command!="previous")throw new Exception("Media alias not normalized");
            command=null;await client.CallAsync("lingdongcapsule","media_control",new{action="invalid"});if(command!=null)throw new Exception("Unlisted media command reached controller");
            var status=JsonSerializer.Serialize((await client.CallAsync("lingdongcapsule","media_status",new{})).Unwrap());if(!status.Contains("Test track"))throw new Exception("Media status missing");
            Console.WriteLine("PASS Independent MSP peers exchange ping, notification, media status and allowlisted control");
        }
        finally{client.Stop();}
        await TestIslandAsync(Path.Combine(directory,"island"));
    }
    private static async Task TestIslandAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
        var settings=new Settings{MspEnabled=true,MediaEnabled=false,NotificationsEnabled=true};
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings,localMspDirectory:directory);
        var peer=new MspNode("island-test-peer",nodesDir:directory);
        try
        {
            window.Show();peer.Start();bool reached=false;
            for(var attempt=0;attempt<30;attempt++)
            {
                try{var ping=JsonSerializer.Serialize((await peer.CallAsync("lingdongcapsule","ping",new{})).Unwrap());if(ping.Contains("lingdongcapsule")){reached=true;break;}}catch(Exception){ }
                await Task.Delay(200);
            }
            if(!reached)throw new Exception("Actual island MSP node was not discovered");
            await peer.CallAsync("lingdongcapsule","notify",new{title="Island MSP fixture",body="Local test only"});
            if(window.LiveState.Notification?.Title!="Island MSP fixture")throw new Exception("Peer notification did not reach actual island state");
            settings.QuietUntil=DateTimeOffset.UtcNow.AddMinutes(30);
            var muted=JsonSerializer.Serialize((await peer.CallAsync("lingdongcapsule","notify",new{title="Should be muted",body=""})).Unwrap());
            if(window.LiveState.Notification!=null || !muted.Contains("false"))throw new Exception("MSP notification bypassed quiet preference");
            Console.WriteLine("PASS Actual island receives MSP notification and rejects it during quiet period");
        }
        finally{peer.Stop();window.Close();}
    }
}
