using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Capsule.App;

public sealed class AudioService : IDisposable
{
    private const int BlockSize=1024;
    private readonly object gate=new();
    private readonly float[] samples=new float[BlockSize];
    private readonly Complex[] transform=new Complex[BlockSize];
    private readonly (double Low,double High)[] ranges=[(40,160),(160,400),(400,1000),(1000,3000),(3000,12000)];
    private MMDevice? device;
    private WasapiLoopbackCapture? capture;
    private int cursor;
    private long transformedAt,receivedAt;
    private double[] bands=new double[5];
    private static readonly double[] Silence=new double[5];
    private DateTimeOffset probedAt;
    private bool disposed;
    public bool HasDevice=>device is not null;
    public string Status {get;private set;}="音频未连接";
    public double[] Bands=>Stopwatch.GetElapsedTime(Interlocked.Read(ref receivedAt)).TotalSeconds<0.7 ? Volatile.Read(ref bands) : Silence;
    public double Volume
    {
        get {try{EnsureDevice();return device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0;}catch{Status="音量暂不可用";return 0;}}
    }
    public bool SetVolume(double volume)
    {
        try{EnsureDevice();if(device is null)return false;device.AudioEndpointVolume.MasterVolumeLevelScalar=(float)Math.Clamp(volume,0,1);return true;}
        catch{Status="音量暂不可用";return false;}
    }
    public void Update(bool enabled)
    {
        if(disposed)return;
        try
        {
            EnsureDevice();
            if(enabled && capture is null && device is not null)
            {
                cursor=0;
                var next=new WasapiLoopbackCapture(device);
                next.DataAvailable+=OnSamples;
                next.RecordingStopped+=OnStopped;
                capture=next;next.StartRecording();Status="系统音频已连接";
            }
            else if(!enabled)StopCapture();
        }
        catch{Status="音频设备暂不可用，将自动重试";StopCapture();}
    }
    private void EnsureDevice()
    {
        if(disposed)return;
        if(device is not null && DateTimeOffset.UtcNow-probedAt<TimeSpan.FromSeconds(5))return;
        probedAt=DateTimeOffset.UtcNow;
        using var devices=new MMDeviceEnumerator();
        var current=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
        if(device?.ID==current.ID){current.Dispose();return;}
        StopCapture();device?.Dispose();device=current;
    }
    private void StopCapture()
    {
        var old=capture;capture=null;
        if(old is null)return;
        old.DataAvailable-=OnSamples;old.RecordingStopped-=OnStopped;
        try{old.StopRecording();}catch{ }
        old.Dispose();Volatile.Write(ref bands,new double[5]);
    }
    private void OnStopped(object? sender,StoppedEventArgs args)
    {
        Status=args.Exception is null ? "音频捕获已停止" : "音频设备已变化，将自动恢复";
        // Disposal happens on the UI health tick, never from the capture callback.
        if(sender is WasapiLoopbackCapture stopped && ReferenceEquals(capture,stopped))needsRestart=true;
    }
    private volatile bool needsRestart;
    public void Tick(bool enabled)
    {
        if(needsRestart){needsRestart=false;StopCapture();device?.Dispose();device=null;}
        Update(enabled);
    }
    private void OnSamples(object? sender,WaveInEventArgs args)
    {
        if(sender is not WasapiLoopbackCapture source)return;
        var format=source.WaveFormat;
        var bytes=format.BitsPerSample/8;
        if(bytes is not (2 or 3 or 4) || format.Channels<1)return;
        var floating=format.Encoding==WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
        lock(gate)
        {
            var stride=bytes*format.Channels;
            for(var offset=0;offset+stride<=args.BytesRecorded;offset+=stride)
            {
                double sample=0;
                for(var channel=0;channel<format.Channels;channel++)
                {
                    var at=offset+channel*bytes;
                    sample+=bytes switch
                    {
                        2=>BitConverter.ToInt16(args.Buffer,at)/32768d,
                        3=>((args.Buffer[at]|args.Buffer[at+1]<<8|args.Buffer[at+2]<<16)<<8>>8)/8388608d,
                        _=>floating ? BitConverter.ToSingle(args.Buffer,at) : BitConverter.ToInt32(args.Buffer,at)/2147483648d
                    };
                }
                samples[cursor++]=(float)(double.IsFinite(sample) ? Math.Clamp(sample/format.Channels,-1,1) : 0);
                if(cursor!=BlockSize)continue;
                cursor=0;
                if(Stopwatch.GetElapsedTime(transformedAt).TotalMilliseconds<32)continue;
                transformedAt=Stopwatch.GetTimestamp();Interlocked.Exchange(ref receivedAt,transformedAt);
                for(var i=0;i<BlockSize;i++){transform[i].X=samples[i]*(float)(0.5-0.5*Math.Cos(2*Math.PI*i/(BlockSize-1)));transform[i].Y=0;}
                FastFourierTransform.FFT(true,10,transform);
                var next=new double[5];
                for(var band=0;band<5;band++)
                {
                    var first=Math.Clamp((int)(ranges[band].Low*BlockSize/format.SampleRate),1,BlockSize/2-1);
                    var last=Math.Clamp((int)(ranges[band].High*BlockSize/format.SampleRate),first+1,BlockSize/2);
                    double peak=0;
                    for(var bin=first;bin<last;bin++)peak=Math.Max(peak,Math.Sqrt(transform[bin].X*transform[bin].X+transform[bin].Y*transform[bin].Y));
                    next[band]=Math.Clamp(Math.Log10(1+peak*100)*0.75,0,1);
                }
                Volatile.Write(ref bands,next);
            }
        }
    }
    public void Dispose(){if(disposed)return;disposed=true;StopCapture();device?.Dispose();device=null;}
}
