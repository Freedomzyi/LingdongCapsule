using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Capsule.Core;

namespace Capsule.App;

internal sealed record SoloSnapshot(bool Connected,LyricsDocument Lyrics,double Position,bool Playing,double? Volume,double[] Bands);
internal sealed class SoloConnection : IDisposable
{
    private readonly object gate=new();
    private readonly SoloState state=new();
    private readonly Uri endpoint;
    private readonly SemaphoreSlim sendGate=new(1,1);
    private CancellationTokenSource? lifetime;
    private ClientWebSocket? socket;
    private Task? loop;
    private bool connected,disposed;
    public SoloConnection(Uri? endpoint=null)
    {
        this.endpoint=endpoint ?? new("ws://127.0.0.1:47290/");
        if(!this.endpoint.IsLoopback || this.endpoint.Scheme!="ws")throw new ArgumentException("Just Solo must use a local WebSocket endpoint");
    }
    public SoloSnapshot Snapshot()
    {
        lock(gate){var now=Environment.TickCount64;return new(connected,state.Lyrics,state.Position(now),state.Playing,state.Volume,state.Spectrum(now).ToArray());}
    }
    public void Configure(bool enabled)
    {
        lock(gate)
        {
            if(disposed || enabled==(lifetime!=null))return;
            if(!enabled){StopLocked();return;}
            var source=new CancellationTokenSource();lifetime=source;loop=Task.Run(()=>RunAsync(source));
        }
    }
    private void StopLocked()
    {
        var source=lifetime;lifetime=null;source?.Cancel();socket?.Abort();socket=null;connected=false;state.Reset();
        if(source!=null){if(loop!=null)_=loop.ContinueWith(_=>source.Dispose(),TaskScheduler.Default);else source.Dispose();}
        loop=null;
    }
    private async Task RunAsync(CancellationTokenSource source)
    {
        var token=source.Token;var delay=1000;
        while(!token.IsCancellationRequested)
        {
            using var client=new ClientWebSocket();
            try
            {
                using(var connect=CancellationTokenSource.CreateLinkedTokenSource(token)){connect.CancelAfter(TimeSpan.FromSeconds(3));await client.ConnectAsync(endpoint,connect.Token);}
                await client.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"hello\",\"client\":\"LingdongCapsule\"}"),WebSocketMessageType.Text,true,token);
                lock(gate){if(lifetime!=source)return;socket=client;connected=true;state.Reset();}delay=1000;
                var buffer=new byte[4096];using var message=new MemoryStream();
                while(client.State==WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var frame=await client.ReceiveAsync(new ArraySegment<byte>(buffer),token);
                    if(frame.MessageType==WebSocketMessageType.Close)break;
                    if(frame.MessageType!=WebSocketMessageType.Text)throw new InvalidDataException("Unexpected binary message");
                    if(message.Length+frame.Count>1024*1024)throw new InvalidDataException("Message size limit exceeded");
                    message.Write(buffer,0,frame.Count);if(!frame.EndOfMessage)continue;
                    var text=new UTF8Encoding(false,true).GetString(message.GetBuffer(),0,(int)message.Length);message.SetLength(0);
                    lock(gate){if(lifetime!=source || token.IsCancellationRequested)return;state.Apply(text,Environment.TickCount64);}
                }
            }
            catch(Exception error) when(error is WebSocketException or OperationCanceledException or IOException or ArgumentException){ }
            finally{lock(gate){if(lifetime==source){socket=null;connected=false;state.Reset();}}}
            if(token.IsCancellationRequested)break;
            try{await Task.Delay(delay,token);}catch(OperationCanceledException){break;}delay=Math.Min(30000,delay*2);
        }
    }
    public async Task<bool> SetVolumeAsync(double value)
    {
        if(!double.IsFinite(value))return false;
        ClientWebSocket? target;CancellationToken token;
        lock(gate){if(!connected || socket==null || lifetime==null)return false;target=socket;token=lifetime.Token;}
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var acquired=false;
        try
        {
            await sendGate.WaitAsync(timeout.Token);acquired=true;
            await target.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{type="volume",value=Math.Clamp(value,0,1)})),WebSocketMessageType.Text,true,timeout.Token);return true;
        }
        catch(Exception error) when(error is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException){return false;}
        finally{if(acquired)sendGate.Release();}
    }
    public void Dispose(){lock(gate){disposed=true;StopLocked();}}
}
