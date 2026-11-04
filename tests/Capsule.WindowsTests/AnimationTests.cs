using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Capsule.App;
using Capsule.Core;

internal static class AnimationTests
{
    internal static async Task RunAsync()
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","animation-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings{MediaEnabled=false,NotificationsEnabled=false,PrivatePreview=false,MotionMs=450,OnlineCoversEnabled=false};
        var window=new MainWindow(suppressOnboarding:true,initialSettings:settings,localPluginDirectory:Path.Combine(root,"plugins"),pluginSettingsWriter:()=>{}){ShowActivated=false};
        var trace=new List<object>();
        try
        {
            window.Show();await Task.Delay(300);((DispatcherTimer)typeof(MainWindow).GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Stop();
            settings.NotificationsEnabled=true;var watch=Stopwatch.StartNew();
            for(var i=0;i<22;i++)
            {
                window.AcceptNotification(new("owned.animation","Own animation fixture",i%2==0 ? "Short" : new string('W',80),"Generated test only",(uint)(i+1)),DateTimeOffset.Now);
                await Task.Delay(50);window.UpdateLayout();
                trace.Add(new{seconds=watch.Elapsed.TotalSeconds,height=window.Height,actualHeight=window.ActualHeight,width=window.Width});
                if(watch.Elapsed.TotalMilliseconds>750 && Math.Abs(window.Height-55)>.01)throw new Exception("Width-only notification updates restarted or stalled height transition");
            }
            await Task.Delay(settings.MotionMs+100);window.UpdateLayout();
            if(Math.Abs(window.Height-55)>.01 || Math.Abs(window.ActualHeight-55)>.5)throw new Exception("Final height did not commit after animation completion");
            var render=typeof(MainWindow).GetMethod("Render",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
            window.LiveState.Dismiss();render.Invoke(window,null);await Task.Delay(550);window.UpdateLayout();
            if(Math.Abs(window.Height-settings.CompactHeight)>.01 || Math.Abs(window.ActualHeight-settings.CompactHeight)>.5)throw new Exception("Collapse did not commit compact height");
            Console.WriteLine("PASS actual notification width churn preserves independent height deadline, final geometry and collapse; own WPF fixtures only; evidence: "+root);
        }
        finally{File.WriteAllText(Path.Combine(root,"geometry.json"),JsonSerializer.Serialize(trace));window.Close();}
    }
}
