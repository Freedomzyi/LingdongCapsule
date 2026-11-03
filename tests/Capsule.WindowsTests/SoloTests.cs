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
        if(!await connection.SetVolumeAsync(.25))throw new Exception("Volume command failed");var volume=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);
        if(!Encoding.UTF8.GetString(buffer,0,volume.Count).Contains("0.25"))throw new Exception("Volume command value incorrect");
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"test disconnect",timeout.Token);
        for(var attempt=0;attempt<30 && connection.Snapshot().Connected;attempt++)await Task.Delay(25);
        if(connection.Snapshot().Connected || connection.Snapshot().Lyrics.Lines.Count!=0)throw new Exception("Disconnected connection retained stale lyrics");
        using var reconnect=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        connection.Configure(false);if(connection.Snapshot().Connected || connection.Snapshot().Lyrics.Lines.Count!=0)throw new Exception("Disabled connection retained stale state");
        await Task.Delay(150);if(connection.Snapshot().Connected)throw new Exception("Canceled reconnect published a stale connection");
        Console.WriteLine("PASS Local handshake, fragmented lyrics, volume, disconnect cleanup, reconnect and cancellation");
        await TestIslandAsync();
    }
    private static async Task TestIslandAsync()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
        var settings=new Settings{MediaSourceKind="justsolo",NotificationsEnabled=false,LyricsEnabled=true,CompositeEnabled=true,MediaFirst=true};
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:settings,localSoloEndpoint:new Uri($"ws://127.0.0.1:{port}/"));
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
            window.UpdateLayout();var clock=(System.Windows.FrameworkElement)window.FindName("Clock");var panel=(System.Windows.FrameworkElement)window.FindName("MediaPanel");
            if(clock.Visibility!=System.Windows.Visibility.Visible || panel.Visibility!=System.Windows.Visibility.Visible || System.Windows.Controls.Grid.GetColumn(panel)!=0 || System.Windows.Controls.Grid.GetColumn(clock)!=1 || window.ActualWidth>settings.Width+.5)throw new Exception("Composite media/clock layout or ordering failed");
            var render=typeof(Capsule.App.MainWindow).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
            foreach(var width in new[]{80d,195d,280d,300d})foreach(var first in new[]{false,true})
            {
                settings.Width=width;settings.MediaFirst=first;render.Invoke(window,null);await Task.Delay(650);window.UpdateLayout();
                var slot=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(clock);
                if(System.Windows.Controls.Grid.GetColumn(panel)!=(first ? 0 : 1) || !double.IsFinite(clock.ActualWidth) || clock.MaxWidth!=Math.Max(0,width-24-110-8) || slot.Width>Math.Max(0,width-24-110)+1 || window.ActualWidth>width+.5)throw new Exception($"Composite width/order boundary failed: requested={width}, first={first}, slot={slot.Width}, max={clock.MaxWidth}, window={window.ActualWidth}");
            }
            settings.CompositeEnabled=false;render.Invoke(window,null);
            if(clock.Visibility!=System.Windows.Visibility.Collapsed || panel.Visibility!=System.Windows.Visibility.Visible)throw new Exception("Disabling composite did not restore exclusive media");
            settings.CompositeEnabled=true;settings.Width=280;settings.MediaFirst=true;render.Invoke(window,null);await Task.Delay(650);window.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
            var evidence=System.IO.Path.Combine("artifacts","verification","composite");System.IO.Directory.CreateDirectory(evidence);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(System.IO.Path.Combine(evidence,"media-clock.png")))encoder.Save(file);
            Console.WriteLine("PASS Composite width 80/195/280/300, both orders, exclusive fallback and WPF render");
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"end integration",timeout.Token);
            for(var attempt=0;attempt<30 && window.LiveState.Media.Available;attempt++)await Task.Delay(100);
            if(window.LiveState.Media.Available || view.FrameProvider?.Invoke().Line!=null)throw new Exception("Disconnected island retained local media or lyrics");
            Console.WriteLine("PASS Actual island renders local Solo lyrics and clears state after disconnect");
        }
        finally{window.Close();}
    }
}
