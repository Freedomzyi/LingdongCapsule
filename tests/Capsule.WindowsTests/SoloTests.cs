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
    }
}
