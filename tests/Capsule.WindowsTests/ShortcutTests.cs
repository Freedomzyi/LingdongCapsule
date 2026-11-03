using Capsule.App;
using Capsule.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class ShortcutTests
{
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    private static HwndSource Source(string name)=>new(new HwndSourceParameters(name){ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0});
    public static async Task RunAsync()
    {
        using var owner=Source("CapsuleShortcutOwnerTest");using var other=Source("CapsuleShortcutConflictTest");
        var free=new List<ShortcutChord>();
        for(var key=124;key<=135;key++)if(RegisterHotKey(other.Handle,0x6D01,7|0x4000,(uint)key)){free.Add(new(7,key));UnregisterHotKey(other.Handle,0x6D01);}
        if(free.Count<7)throw new Exception("Not enough unused test-only Ctrl+Alt+Shift+F13..F24 combinations");
        var settings=new Settings{MediaShortcutsEnabled=true,IslandShortcutEnabled=true};var actions=Enum.GetValues<ShortcutAction>();
        for(var index=0;index<actions.Length;index++)settings.Shortcuts[actions[index]]=free[index];
        var reloaded=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;reloaded.Normalize();
        if(!actions.All(action=>reloaded.Shortcuts[action]==settings.Shortcuts[action]) || !reloaded.IslandShortcutEnabled || !reloaded.MediaShortcutsEnabled)throw new Exception("Shortcut settings did not survive JSON round trip");
        using(var service=new GlobalShortcuts(settings))
        {
            var received=0;service.Pressed+=_=>received++;
            owner.AddHook((IntPtr window,int message,IntPtr wparam,IntPtr lparam,ref bool handled)=>{if(message==0x0312)handled=service.Handle(wparam.ToInt32());return IntPtr.Zero;});
            service.Attach(owner.Handle);if(service.RegisteredCount!=6 || service.Error.Length>0)throw new Exception("Native shortcut registration failed");
            PostMessage(owner.Handle,0x0312,new IntPtr(GlobalShortcuts.NativeId(ShortcutAction.ToggleIsland)),IntPtr.Zero);await Task.Delay(100);
            if(received!=1)throw new Exception("Native message did not dispatch action");
            service.Suspend();
            if(service.RegisteredCount!=0 || service.Handle(GlobalShortcuts.NativeId(ShortcutAction.ToggleIsland)))throw new Exception("Recording suspension still dispatches actions");
            if(!RegisterHotKey(other.Handle,0x6D02,free[0].Modifiers|0x4000,(uint)free[0].Key))throw new Exception("Recording did not release native registrations");UnregisterHotKey(other.Handle,0x6D02);
            if(!RegisterHotKey(other.Handle,0x6D03,free[6].Modifiers|0x4000,(uint)free[6].Key))throw new Exception("Conflict fixture could not register");
            try
            {
                var before=settings.Shortcuts[ShortcutAction.ToggleIsland];
                if(service.SetBinding(ShortcutAction.ToggleIsland,free[6]).Length==0 || settings.Shortcuts[ShortcutAction.ToggleIsland]!=before)throw new Exception("External conflict did not preserve old chord");
                if(service.SetBinding(ShortcutAction.ToggleIsland,free[0]).Length==0)throw new Exception("Internal duplicate chord accepted");
            }
            finally{UnregisterHotKey(other.Handle,0x6D03);}
            service.Resume();if(service.RegisteredCount!=6)throw new Exception("Shortcut registrations did not resume");
            settings.MediaShortcutsEnabled=false;service.Apply();if(service.RegisteredCount!=1)throw new Exception("Media switch disabled island shortcut");
            settings.MediaShortcutsEnabled=true;settings.IslandShortcutEnabled=false;service.Apply();if(service.RegisteredCount!=5)throw new Exception("Island switch disabled media shortcuts");
            if(service.SetBinding(ShortcutAction.PlayPause,new(0,0)).Length>0 || service.RegisteredCount!=4)throw new Exception("Clearing shortcut did not release system binding");
            var cleared=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;cleared.Normalize();if(!cleared.Shortcuts[ShortcutAction.PlayPause].IsEmpty)throw new Exception("Cleared shortcut reverted to factory default");
        }
        if(!RegisterHotKey(other.Handle,0x6D04,free[1].Modifiers|0x4000,(uint)free[1].Key))throw new Exception("Disposal leaked registration");UnregisterHotKey(other.Handle,0x6D04);
        Console.WriteLine("PASS Native registration, conflict rollback, recording suspension, independent switches, dispatch and cleanup");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
        var themedCard=Design.Card(new TextBlock());var themeWindow=new Window{Content=themedCard,Width=100,Height=100,ShowActivated=false,ShowInTaskbar=false};themeWindow.Show();Design.ApplyTheme(0);await Task.Delay(50);
        var dark=((System.Windows.Media.SolidColorBrush)themedCard.Background).Color;
        Design.ApplyTheme(1);await Task.Delay(50);var light=((System.Windows.Media.SolidColorBrush)themedCard.Background).Color;
        if(dark.R>=light.R || light.R!=255)throw new Exception("Existing theme card did not update dynamically");
        themeWindow.Hide();Design.ApplySystemTheme();Console.WriteLine("PASS Existing card resources switch from dark to light without recreating controls");
        var islandSettings=new Settings{MediaEnabled=false,NotificationsEnabled=false,AutoHide=true,FocusAutoHide=true,IslandShortcutEnabled=true,ClockLayout=1,HardwareEnabled=true,HardwareFirst=true,ThemeMode=1};islandSettings.Shortcuts[ShortcutAction.ToggleIsland]=free[5];
        var island=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:islandSettings);
        try
        {
            island.Show();await Task.Delay(900);
            islandSettings.BackgroundOpacity=50;island.SetStandby(false);
            if(((System.Windows.Media.SolidColorBrush)((Border)island.FindName("Shell")).Background).Color.A!=128 || ((TextBlock)island.FindName("ClockTime")).Opacity!=1)throw new Exception("Background opacity affected text or did not apply");
            islandSettings.BackgroundOpacity=96;island.SetStandby(false);
            if(((System.Windows.Media.SolidColorBrush)((Border)island.FindName("Shell")).Background).Color.R<200 || ((System.Windows.Media.SolidColorBrush)((TextBlock)island.FindName("ClockTime")).Foreground).Color.R>100)throw new Exception("Light island theme has unreadable foreground/background");
            var date=(TextBlock)island.FindName("ClockDate");var clock=(FrameworkElement)island.FindName("Clock");
            if(island.LiveModuleOrder.Take(2).SequenceEqual(new[]{"hardware","clock"})==false)throw new Exception("Configured hardware-first order was not applied");
            if(date.Visibility!=Visibility.Visible || date.Text!=DateTime.Now.ToString("MM/dd") || clock.DesiredSize.Width>island.ActualWidth-24)throw new Exception("Time/date layout exceeds compact island");
            if(!island.LiveVisibility.Hidden || ((Button)island.FindName("WakeButton")).Visibility!=Visibility.Visible)throw new Exception("Idle auto-hide did not retain wake button");
            ((Button)island.FindName("WakeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            if(island.LiveVisibility.Hidden)throw new Exception("Wake button did not reveal island");
            await Task.Delay(1300);
            island.SetStandby(true);if(((FrameworkElement)island.FindName("HardwarePanel")).Visibility!=Visibility.Collapsed || clock.Visibility!=Visibility.Visible)throw new Exception("Time standby did not isolate clock component");
            islandSettings.StandbyScene=2;island.SetStandby(true);if(clock.Visibility!=Visibility.Collapsed)throw new Exception("Blank standby still shows clock");
            island.SetStandby(false);if(((FrameworkElement)island.FindName("HardwarePanel")).Visibility!=Visibility.Visible)throw new Exception("Leaving standby did not restore component choice");
            islandSettings.Width=80;island.SetStandby(false);
            for(var attempt=0;attempt<30 && island.ActualWidth>80.5;attempt++)await Task.Delay(100);
            if(island.ActualWidth>80.5)throw new Exception("Compact components exceeded configured global width");
            var autoHide=islandSettings.AutoHide;islandSettings.AutoHide=false;
            islandSettings.Width=280;island.SetStandby(false);
            for(var attempt=0;attempt<40 && island.CompactHardwareSample is not{Cpu:>=0 and <=100,Memory:>=0 and <=100};attempt++)await Task.Delay(100);
            if(island.CompactHardwareSample is not{Cpu:>=0 and <=100,Memory:>=0 and <=100} || ((FrameworkElement)island.FindName("HardwarePanel")).Visibility!=Visibility.Visible)throw new Exception("Compact hardware component did not receive valid real readings");
            islandSettings.AutoHide=autoHide;
            island.SetIslandHidden(true);
            for(var attempt=0;attempt<30 && ((FrameworkElement)island.FindName("IslandSurface")).Opacity>0.001;attempt++)await Task.Delay(100);
            if(!island.ManuallyHidden || ((Button)island.FindName("WakeButton")).Visibility!=Visibility.Collapsed || ((FrameworkElement)island.FindName("IslandSurface")).Opacity>0.001)throw new Exception("Manual hide left visible island UI");
            var window=new WindowInteropHelper(island).Handle;var point=island.PointToScreen(new Point(10,10));var coordinates=new IntPtr(((long)(ushort)(short)point.Y<<16)|(ushort)(short)point.X);
            if(SendMessage(window,0x0084,IntPtr.Zero,coordinates)!=new IntPtr(-1))throw new Exception("Hidden island did not pass native hit test through");
            if(island.LiveShortcuts.RegisteredCount!=1)throw new Exception("Hidden island lost shortcut registration");
            PostMessage(window,0x0312,new IntPtr(GlobalShortcuts.NativeId(ShortcutAction.ToggleIsland)),IntPtr.Zero);await Task.Delay(150);
            if(island.ManuallyHidden || island.LiveVisibility.Hidden)throw new Exception("Native shortcut message did not restore hidden island");
            Console.WriteLine("PASS Actual WPF auto-hide, wake, complete manual hide, native hit-test transparency and native-message restoration");
        }
        finally{themeWindow.Close();island.Close();}
    }
}
