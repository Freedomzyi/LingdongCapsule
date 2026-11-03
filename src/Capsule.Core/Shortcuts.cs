namespace Capsule.Core;

public enum ShortcutAction{PlayPause,Previous,Next,SeekBackward,SeekForward,ToggleIsland}
public sealed record ShortcutChord(uint Modifiers,int Key)
{
    public bool IsEmpty=>Key==0;
    public bool IsValid=>IsEmpty ? Modifiers==0 : Key is >0 and <=255 && Modifiers is >0 and <=15 && Key is not (16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165);
    public static ShortcutChord Default(ShortcutAction action)=>action switch
    {
        ShortcutAction.PlayPause=>new(3,32),ShortcutAction.Previous=>new(3,37),ShortcutAction.Next=>new(3,39),ShortcutAction.SeekBackward=>new(1,37),ShortcutAction.SeekForward=>new(1,39),_=>new(1,73)
    };
}

public readonly record struct IslandVisibilityInput(bool ManualHidden,bool AutoHide,bool FocusHide,bool PauseHide,bool FullscreenHide,bool Fullscreen,bool MediaAvailable,bool Playing,bool Notification,bool Expanded,bool Awake,bool Passthrough,bool PointerOver);
public readonly record struct IslandVisibilityResult(bool Hidden,bool ShowWake,bool PassThrough);
public static class IslandVisibility
{
    public static IslandVisibilityResult Resolve(IslandVisibilityInput input)
    {
        if(input.ManualHidden)return new(true,false,true);
        var automatic=input.AutoHide && (input.FullscreenHide && input.Fullscreen || (input.MediaAvailable ? input.PauseHide && !input.Playing : input.FocusHide));
        var protectedInteraction=input.Notification || input.Expanded || input.Awake;
        var hidden=automatic && !protectedInteraction;
        var pass=input.Passthrough && input.PointerOver && !protectedInteraction;
        return new(hidden,hidden || pass,hidden || pass);
    }
}
