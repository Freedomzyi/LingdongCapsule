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
            client.Start();string? ping=null;
            for(var attempt=0;attempt<30;attempt++)
            {
                try{ping=JsonSerializer.Serialize((await client.CallAsync("lingdongcapsule","ping",new{})).Unwrap());if(ping.Contains("lingdongcapsule"))break;}catch(Exception){ }
                await Task.Delay(200);
            }
            if(ping==null || !ping.Contains("lingdongcapsule"))throw new Exception("MSP peers did not exchange ping");
            await client.CallAsync("lingdongcapsule","notify",new{title="MSP test",body="Independent local fixture"});
            if(received?.Title!="MSP test")throw new Exception("MSP notification did not reach adapter");
            await client.CallAsync("lingdongcapsule","media_control",new{action="prev"});if(command!="previous")throw new Exception("Media alias not normalized");
            command=null;await client.CallAsync("lingdongcapsule","media_control",new{action="invalid"});if(command!=null)throw new Exception("Unlisted media command reached controller");
            var status=JsonSerializer.Serialize((await client.CallAsync("lingdongcapsule","media_status",new{})).Unwrap());if(!status.Contains("Test track"))throw new Exception("Media status missing");
            Console.WriteLine("PASS Independent MSP peers exchange ping, notification, media status and allowlisted control");
        }
        finally{client.Stop();}
    }
}
