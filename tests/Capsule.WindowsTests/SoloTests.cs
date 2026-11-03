using Capsule.App;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

internal static class SoloTests
{
    internal static async Task RunAsync()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var connection=new SoloConnection(new Uri($"ws://127.0.0.1:{port}/"));connection.Configure(true);
        using var client=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));using var stream=client.GetStream();
        using var reader=new System.IO.StreamReader(stream,Encoding.ASCII,false,1024,leaveOpen:true);string? key=null;
        while(true){var line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));if(string.IsNullOrEmpty(line))break;if(line.StartsWith("Sec-WebSocket-Key:",StringComparison.OrdinalIgnoreCase))key=line.Split(':',2)[1].Trim();}
        if(key==null)throw new Exception("Missing WebSocket handshake key");
        var accept=Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
        using var socket=WebSocket.CreateFromStream(stream,true,null,TimeSpan.FromSeconds(30));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));var buffer=new byte[4096];
        var hello=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);if(!Encoding.UTF8.GetString(buffer,0,hello.Count).Contains("LingdongCapsule"))throw new Exception("Wrong client identity");
        var payload=Encoding.UTF8.GetBytes("{\"type\":\"init\",\"lyrics\":[{\"time\":1000,\"text\":\"Local test\"}]}");
        await socket.SendAsync(new ArraySegment<byte>(payload,0,10),WebSocketMessageType.Text,false,timeout.Token);await socket.SendAsync(new ArraySegment<byte>(payload,10,payload.Length-10),WebSocketMessageType.Text,true,timeout.Token);
        for(var attempt=0;attempt<30 && connection.Snapshot().Lyrics.Lines.Count==0;attempt++)await Task.Delay(50);
        if(!connection.Snapshot().Connected || connection.Snapshot().Lyrics.Lines.Count!=1)throw new Exception("Fragmented local lyric message not received");
        if(!await connection.SetVolumeAsync(.25) || connection.Snapshot().Volume!=.25)throw new Exception("Volume command or optimistic local value failed");var volume=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);
        if(!Encoding.UTF8.GetString(buffer,0,volume.Count).Contains("0.25"))throw new Exception("Volume command value incorrect");
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"test disconnect",timeout.Token);
        for(var attempt=0;attempt<30 && connection.Snapshot().Connected;attempt++)await Task.Delay(25);
        if(connection.Snapshot().Connected || connection.Snapshot().Lyrics.Lines.Count!=0)throw new Exception("Disconnected connection retained stale lyrics");
        using var reconnect=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        connection.Configure(false);if(connection.Snapshot().Connected || connection.Snapshot().Lyrics.Lines.Count!=0)throw new Exception("Disabled connection retained stale state");
        await Task.Delay(150);if(connection.Snapshot().Connected)throw new Exception("Canceled reconnect published a stale connection");
        Console.WriteLine("PASS Local handshake, fragmented lyrics, volume, disconnect cleanup, reconnect and cancellation");
        await TestIslandAsync();
        await TestNativeFallbackAsync();
    }
    private static async Task TestNativeFallbackAsync()
    {
        using var publisher=await OwnedMediaPublisher.StartAsync("LingdongCapsule.Owned.justsolo");
        var directory=System.IO.Path.Combine(Environment.CurrentDirectory,"artifacts","solo-native-tests",Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(directory);var path=System.IO.Path.Combine(directory,"owned-thumbnail.png");
        var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,0,130,255},4);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=System.IO.File.Create(path))encoder.Save(output);await publisher.SetThumbnailAsync(path);
        using var unavailable=new TcpListener(IPAddress.Loopback,0);unavailable.Start();var port=((IPEndPoint)unavailable.LocalEndpoint).Port;
        var settings=new Settings{MediaSourceKind="other",ManualMediaSource="LingdongCapsule.Owned.justsolo",NotificationsEnabled=false,LyricsEnabled=false,OnlineCoversEnabled=false,ShowSpectrum=false};
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings,localSoloEndpoint:new Uri($"ws://127.0.0.1:{port}/")){ShowActivated=false};
        try
        {
            window.Show();using var attempt=await unavailable.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(6));
            for(var index=0;index<60 && (!window.LiveState.Media.Available || window.EffectiveCover==null);index++)await Task.Delay(100);
            if(window.LiveSolo.Connected || !window.LiveState.Media.Available || window.LiveState.Media.Title!=publisher.Title || window.EffectiveCover==null)throw new Exception("Unavailable Solo lyric server hid matched native metadata or thumbnail");
            settings.ManualMediaSource="";var select=typeof(Capsule.App.MainWindow).GetMethod("SoloSourceSelected",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            if(!(bool)select.Invoke(window,new object[]{"LingdongCapsule.Owned.justsolo"})! || (bool)select.Invoke(window,new object[]{"OtherPlayer.exe"})!)throw new Exception("Automatic media selection did not identify only Solo sources");
            Console.WriteLine("PASS Matched Just Solo SMTC retains metadata/artwork without lyric server; manual connection and automatic-source policy verified with owned native publisher");
        }
        finally{window.Close();}
    }
    private static async Task TestIslandAsync()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;Design.ApplySystemTheme();
        var settings=new Settings{MediaSourceKind="justsolo",NotificationsEnabled=false,LyricsEnabled=true,CompositeEnabled=true,MediaFirst=true};
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings,localSoloEndpoint:new Uri($"ws://127.0.0.1:{port}/"),pluginSettingsWriter:()=>{});
        try
        {
            window.Show();using var client=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));using var stream=client.GetStream();
            using var reader=new System.IO.StreamReader(stream,Encoding.ASCII,false,1024,leaveOpen:true);string? key=null;
            while(true){var line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));if(string.IsNullOrEmpty(line))break;if(line.StartsWith("Sec-WebSocket-Key:",StringComparison.OrdinalIgnoreCase))key=line.Split(':',2)[1].Trim();}
            if(key==null)throw new Exception("Island connection missing handshake");
            var accept=Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
            using var socket=WebSocket.CreateFromStream(stream,true,null,TimeSpan.FromSeconds(30));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var buffer=new byte[4096];await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);
            foreach(var message in new[]{"{\"type\":\"init\",\"lyrics\":[{\"time\":1000,\"text\":\"Live test\"}]}","{\"type\":\"progress\",\"position\":2000}","{\"type\":\"playback\",\"status\":\"playing\"}"})await socket.SendAsync(Encoding.UTF8.GetBytes(message),WebSocketMessageType.Text,true,timeout.Token);
            var view=(LyricView)window.FindName("LiveLyrics");
            for(var attempt=0;attempt<30 && (!window.LiveState.Media.Available || view.Visibility!=System.Windows.Visibility.Visible);attempt++)await Task.Delay(100);
            if(!window.LiveState.Media.Playing || view.FrameProvider?.Invoke().Line?.Text!="Live test" || view.Visibility!=System.Windows.Visibility.Visible)throw new Exception("Local protocol lyrics did not reach actual island");
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"volume\",\"value\":0.25}"),WebSocketMessageType.Text,true,timeout.Token);
            for(var attempt=0;attempt<30 && window.LiveSolo.Volume!=.25;attempt++)await Task.Delay(50);
            if(window.LiveSolo.Volume!=.25)throw new Exception("Local volume fixture did not reach actual island");
            using(var systemAudio=new AudioService())
            {
                var before=systemAudio.Volume;((System.Windows.Controls.Slider)window.FindName("VolumeBar")).Value=.35;
                var volumeFrame=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);
                using var volumeJson=System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(buffer,0,volumeFrame.Count));
                if(volumeJson.RootElement.GetProperty("type").GetString()!="volume" || Math.Abs(volumeJson.RootElement.GetProperty("value").GetDouble()-.35)>.0001 || Math.Abs(systemAudio.Volume-before)>.0001)throw new Exception("Solo volume slider failed local command or changed system master volume");
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"volume\",\"value\":0.35}"),WebSocketMessageType.Text,true,timeout.Token);
            }
            window.UpdateLayout();var clock=(System.Windows.FrameworkElement)window.FindName("Clock");var panel=(System.Windows.FrameworkElement)window.FindName("MediaPanel");
            if(clock.Visibility!=System.Windows.Visibility.Visible || panel.Visibility!=System.Windows.Visibility.Visible || !window.LiveModuleOrder.SequenceEqual(new[]{"media","clock"}) || window.ActualWidth>settings.Width+.5)throw new Exception("Composite media/clock layout or ordering failed");
            var render=typeof(Capsule.App.MainWindow).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
            foreach(var width in new[]{80d,195d,280d,300d})foreach(var first in new[]{false,true})
            {
                settings.Width=width;settings.MediaFirst=first;render.Invoke(window,null);await Task.Delay(650);window.UpdateLayout();
                var slot=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot((System.Windows.FrameworkElement)clock.Parent);
                if(!window.LiveModuleOrder.SequenceEqual(first ? new[]{"media","clock"} : new[]{"clock","media"}) || !double.IsFinite(clock.ActualWidth) || slot.Width>Math.Max(0,width-26-110)+1 || window.ActualWidth>width+.5)throw new Exception($"Composite width/order boundary failed: requested={width}, first={first}, slot={slot.Width}, window={window.ActualWidth}");
            }
            settings.CompositeEnabled=false;render.Invoke(window,null);
            if(clock.Visibility!=System.Windows.Visibility.Collapsed || panel.Visibility!=System.Windows.Visibility.Visible)throw new Exception("Disabling composite did not restore exclusive media");
            settings.CompositeEnabled=true;settings.Width=280;settings.MediaFirst=true;render.Invoke(window,null);await Task.Delay(650);window.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
            var evidence=System.IO.Path.Combine("artifacts","verification","composite");System.IO.Directory.CreateDirectory(evidence);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(System.IO.Path.Combine(evidence,"media-clock.png")))encoder.Save(file);
            Console.WriteLine("PASS Composite width 80/195/280/300, both orders, exclusive fallback and WPF render");
            var mixed=new[]{"plugin:fixture.second","hardware","media","plugin:fixture.clock","clock"};
            var directory=System.IO.Path.Combine(evidence,Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(directory);var second=System.IO.Path.Combine(directory,"fixture-second.dll");System.IO.File.Copy(typeof(FixturePlugin).Assembly.Location,second);
            window.LoadPlugin(typeof(FixturePlugin).Assembly.Location,save:false);window.LoadPlugin(second,save:false);settings.HardwareEnabled=true;settings.ComponentOrder=mixed.ToList();render.Invoke(window,null);await Task.Delay(700);window.UpdateLayout();
            if(!window.LiveModuleOrder.SequenceEqual(mixed))throw new Exception("Native modules and individual plugins were not interleaved");
            var previousRight=0d;
            foreach(var id in mixed)
            {
                var element=id switch{"clock"=>clock,"hardware"=>(System.Windows.FrameworkElement)window.FindName("HardwarePanel"),"media"=>panel,_=>window.LivePluginWidgets[Array.IndexOf(new[]{"fixture.clock","fixture.second"},id[7..])]};
                var host=(System.Windows.FrameworkElement)element.Parent;var left=host.TransformToAncestor(window).Transform(new System.Windows.Point()).X;
                if(left<previousRight-.5 || left+host.ActualWidth>window.ActualWidth+.5)throw new Exception("Mixed component columns overlap or exceed island bounds");previousRight=left+host.ActualWidth;
            }
            settings.HardwareEnabled=false;render.Invoke(window,null);if(window.LiveModuleOrder.Contains("hardware"))throw new Exception("Disabled hardware retained a visible slot");
            settings.HardwareEnabled=true;window.SetStandby(true);if(!window.LiveModuleOrder.SequenceEqual(new[]{"clock"}))throw new Exception("Standby failed to isolate clock from mixed modules");
            window.SetStandby(false);if(!window.LiveModuleOrder.SequenceEqual(mixed))throw new Exception("Leaving standby lost mixed module order");
            var click=typeof(Capsule.App.MainWindow).GetMethod("HandleIslandClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;click.Invoke(window,null);
            if(!window.LiveModuleOrder.SequenceEqual(new[]{"media"}))throw new Exception("Expanded media did not isolate its controls");click.Invoke(window,null);
            if(!window.LiveModuleOrder.SequenceEqual(mixed))throw new Exception("Collapsing media lost mixed module order");
            window.MoveDisplayComponent("clock",-1);if(!window.LiveModuleOrder.SequenceEqual(new[]{"plugin:fixture.second","hardware","media","clock","plugin:fixture.clock"}))throw new Exception("Unified module move did not alter actual layout");
            Console.WriteLine("PASS Interleaved media/hardware/clock/two plugins, non-overlap, disabled slots, standby and expansion restore");
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"end integration",timeout.Token);
            for(var attempt=0;attempt<30 && window.LiveState.Media.Available;attempt++)await Task.Delay(100);
            if(window.LiveState.Media.Available || view.FrameProvider?.Invoke().Line!=null)throw new Exception("Disconnected island retained local media or lyrics");
            Console.WriteLine("PASS Actual island renders local Solo lyrics and clears state after disconnect");
        }
        finally{window.Close();}
    }
}
