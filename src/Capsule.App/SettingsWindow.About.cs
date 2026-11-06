using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

public sealed partial class SettingsWindow
{
    internal static string BuildVersion=>typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
    private static string DisplayVersion
    {
        get{var parts=BuildVersion.Split('+',2);return parts[0]+(parts.Length>1 ? " · "+parts[1][..Math.Min(7,parts[1].Length)] : "")+" · MIT";}
    }
    internal const string ContactEmail="freedomzy@agent.qq.com";
    internal const string DouyinAccount="Freedomzyi";

    internal static ProcessStartInfo? CreateUninstallCommand(string directory)
    {
        var root=Path.GetFullPath(directory);
        if(!File.Exists(Path.Combine(root,"installed-files.json")))return null;
        var executable=Path.Combine(root,"LingdongCapsule.exe");
        if(!File.Exists(executable))throw new IOException("程序文件缺失，请重新安装后再卸载。");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
        start.ArgumentList.Add("--uninstall");return start;
    }

    private void AddMaintenanceSection(StackPanel panel)
    {
        Heading(panel,"应用维护");
        var section=new StackPanel();
        var note=new TextBlock{Text="卸载前会再次确认，个人设置会保留。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
        Design.Bind(note,TextBlock.ForegroundProperty,"TextSecondary");section.Children.Add(note);
        var uninstall=new Button{Content="卸载灵动胶囊",Padding=new Thickness(12,10,12,10)};
        System.Windows.Automation.AutomationProperties.SetName(uninstall,"卸载灵动胶囊");
        uninstall.Click+=(_,_)=>
        {
            try
            {
                var command=CreateUninstallCommand(AppContext.BaseDirectory);
                if(command==null){Design.PresentMessage("当前为便携版","便携版无需卸载。请从托盘退出灵动胶囊，再删除解压目录；个人设置会保留。");return;}
                using var process=Process.Start(command) ?? throw new IOException("卸载界面未能启动，请稍后重试。");
            }
            catch(Exception error){Design.PresentMessage("无法打开卸载界面",error.Message);}
        };
        section.Children.Add(uninstall);panel.Children.Add(Design.Card(section));
    }

    private void AddAuthorSection(StackPanel panel)
    {
        Heading(panel,"联系作者");
        var contacts=new StackPanel();
        void Contact(string label,string value)
        {
            var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var text=new StackPanel();var caption=new TextBlock{Text=label,FontSize=12,Margin=new Thickness(0,0,0,3)};Design.Bind(caption,TextBlock.ForegroundProperty,"TextSecondary");text.Children.Add(caption);
            var address=new TextBlock{Text=value,FontSize=14,TextWrapping=TextWrapping.Wrap};Design.Bind(address,TextBlock.ForegroundProperty,"TextPrimary");text.Children.Add(address);row.Children.Add(text);
            var copy=new Button{Content="复制",CommandParameter=value,Padding=new Thickness(12,7,12,7),Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(copy,1);System.Windows.Automation.AutomationProperties.SetName(copy,"复制"+label);
            copy.Click+=(_,_)=>{try{Clipboard.SetText(value);copy.Content="已复制";}catch(ExternalException){Design.PresentMessage("暂时无法复制","剪贴板正忙，请稍后重试。");}};
            row.Children.Add(copy);contacts.Children.Add(row);
        }
        Contact("联系邮箱",ContactEmail);Contact("抖音号",DouyinAccount);panel.Children.Add(Design.Card(contacts));
        Heading(panel,"赞赏支持");
        var support=new StackPanel();var description=new TextBlock{Text="如果你喜欢灵动胶囊，欢迎赞赏支持，鼓励我继续优化。",FontSize=13,TextWrapping=TextWrapping.Wrap,LineHeight=21,Margin=new Thickness(0,0,0,12)};Design.Bind(description,TextBlock.ForegroundProperty,"TextSecondary");support.Children.Add(description);
        var open=new Button{Content="查看微信赞赏码",Padding=new Thickness(12,10,12,10)};System.Windows.Automation.AutomationProperties.SetName(open,"查看微信赞赏码");open.Click+=(_,_)=>{var window=new SupportWindow{Owner=this};window.ShowDialog();};support.Children.Add(open);panel.Children.Add(Design.Card(support));
    }
}
