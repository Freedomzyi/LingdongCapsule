using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Capsule.Core;

namespace Capsule.App;

internal static class ShortcutRecorder
{
    public static bool Show(Window owner,GlobalShortcuts shortcuts,ShortcutAction action)
    {
        var window=new Window{Owner=owner,Title="录制快捷键",Width=360,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=new("Microsoft YaHei UI")};
        var panel=new StackPanel{Margin=new Thickness(8)};
        var title=new TextBlock{Text=GlobalShortcuts.Label(action),FontSize=18,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,8,0,12)};Design.Bind(title,TextBlock.ForegroundProperty,"TextPrimary");panel.Children.Add(title);
        var hint=new TextBlock{Text="按下组合键…\nEsc 取消 · Backspace 清空",TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=13,LineHeight=22,Margin=new Thickness(0,0,0,18)};Design.Bind(hint,TextBlock.ForegroundProperty,"TextSecondary");panel.Children.Add(hint);
        var cancel=new Button{Content="取消",IsCancel=true,Height=38};cancel.Click+=(_,_)=>window.DialogResult=false;panel.Children.Add(cancel);window.Content=Design.Card(panel);
        window.PreviewKeyDown+=(_,args)=>
        {
            args.Handled=true;if(args.IsRepeat)return;
            var key=args.Key==Key.System ? args.SystemKey : args.Key==Key.ImeProcessed ? args.ImeProcessedKey : args.Key;
            if(key==Key.Escape){window.DialogResult=false;return;}
            if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
            var modifiers=(uint)Keyboard.Modifiers & 15;
            var chord=key==Key.Back && modifiers==0 ? new ShortcutChord(0,0) : new ShortcutChord(modifiers,KeyInterop.VirtualKeyFromKey(key));
            var error=shortcuts.SetBinding(action,chord);
            if(error.Length>0){hint.Text=error+"\n请重录，或按 Esc 取消。";return;}
            window.DialogResult=true;
        };
        window.Deactivated+=(_,_)=>{if(window.IsVisible)window.DialogResult=false;};
        shortcuts.Suspend();try{return window.ShowDialog()==true;}finally{shortcuts.Resume();}
    }
}
