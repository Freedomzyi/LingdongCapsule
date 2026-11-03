using System.Runtime.InteropServices;
using System.Windows.Input;
using Capsule.Core;

namespace Capsule.App;

internal sealed class GlobalShortcuts : IDisposable
{
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr window,int id);
    private const int FirstId=0x6C20,ProbeId=0x6C30;
    private IntPtr window;
    private readonly Settings settings;
    private readonly HashSet<ShortcutAction> registered=[];
    private bool suspended,disposed;
    public string Error{get;private set;}="";
    public bool Suspended=>suspended;
    internal int RegisteredCount=>registered.Count;
    public event Action<ShortcutAction>? Pressed;
    public GlobalShortcuts(Settings settings)=>this.settings=settings;
    public static string Label(ShortcutAction action)=>action switch{ShortcutAction.PlayPause=>"播放 / 暂停",ShortcutAction.Previous=>"上一首",ShortcutAction.Next=>"下一首",ShortcutAction.SeekBackward=>"快退 3 秒",ShortcutAction.SeekForward=>"快进 3 秒",_=>"隐藏 / 唤出灵动胶囊"};
    public static string Format(ShortcutChord chord)
    {
        if(chord.IsEmpty)return "未设置";
        var names=new List<string>();if((chord.Modifiers&2)!=0)names.Add("Ctrl");if((chord.Modifiers&1)!=0)names.Add("Alt");if((chord.Modifiers&4)!=0)names.Add("Shift");if((chord.Modifiers&8)!=0)names.Add("Win");
        names.Add(chord.Key switch{32=>"Space",37=>"←",38=>"↑",39=>"→",40=>"↓",>=48 and <=57 or >=65 and <=90=>((char)chord.Key).ToString(),>=112 and <=135=>"F"+(chord.Key-111),_=>KeyInterop.KeyFromVirtualKey(chord.Key).ToString()});
        return string.Join(" + ",names);
    }
    public void Attach(IntPtr handle){UnregisterAll();window=handle;Apply();}
    private bool Enabled(ShortcutAction action)=>action==ShortcutAction.ToggleIsland ? settings.IslandShortcutEnabled : settings.MediaShortcutsEnabled;
    public void Apply()
    {
        UnregisterAll();Error="";if(disposed || suspended || window==IntPtr.Zero)return;
        var used=new Dictionary<ShortcutChord,ShortcutAction>();
        foreach(var action in Enum.GetValues<ShortcutAction>())
        {
            var chord=settings.Shortcuts[action];if(!Enabled(action) || chord.IsEmpty)continue;
            if(!chord.IsValid){Error+=$"{Label(action)}：组合无效。\n";continue;}
            if(used.TryGetValue(chord,out var other)){Error+=$"{Label(action)}与{Label(other)}重复。\n";continue;}
            used[chord]=action;
            if(RegisterHotKey(window,FirstId+(int)action,chord.Modifiers|0x4000,(uint)chord.Key))registered.Add(action);
            else Error+=$"{Format(chord)} 被其他程序占用或由系统保留，{Label(action)}未启用。\n";
        }
        Error=Error.TrimEnd();
    }
    public void Suspend(){suspended=true;UnregisterAll();}
    public void Resume(){suspended=false;Apply();}
    public string SetBinding(ShortcutAction action,ShortcutChord candidate)
    {
        if(!candidate.IsValid)return "请搭配 Ctrl / Alt / Shift / Win，主键不能是修饰键。";
        foreach(var other in Enum.GetValues<ShortcutAction>())if(other!=action && !candidate.IsEmpty && settings.Shortcuts[other]==candidate)return $"已用于“{Label(other)}”。";
        var wasSuspended=suspended;var previous=settings.Shortcuts[action];Suspend();
        try
        {
            if(!candidate.IsEmpty && window!=IntPtr.Zero)
            {
                if(!RegisterHotKey(window,ProbeId,candidate.Modifiers|0x4000,(uint)candidate.Key))return $"{Format(candidate)} 被其他程序占用或由系统保留。";
                UnregisterHotKey(window,ProbeId);
            }
            settings.Shortcuts[action]=candidate;
            if(!wasSuspended)
            {
                Resume();
                if(Enabled(action) && !candidate.IsEmpty && !registered.Contains(action)){settings.Shortcuts[action]=previous;Apply();return "快捷键刚被其他程序占用，已恢复原组合。";}
            }
            return "";
        }
        finally{if(!wasSuspended && suspended)Resume();}
    }
    public bool Handle(int id)
    {
        var action=(ShortcutAction)(id-FirstId);
        if(disposed || suspended || !registered.Contains(action))return false;
        Pressed?.Invoke(action);return true;
    }
    private void UnregisterAll(){if(window!=IntPtr.Zero)foreach(var action in registered)UnregisterHotKey(window,FirstId+(int)action);registered.Clear();}
    public void Dispose(){disposed=true;UnregisterAll();window=IntPtr.Zero;}
    internal static int NativeId(ShortcutAction action)=>FirstId+(int)action;
}
