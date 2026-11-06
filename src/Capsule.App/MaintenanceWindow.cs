using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal enum MaintenanceState { Welcome,Location,Working,Completed,Failed }
internal sealed class MaintenanceWindow : Window
{
    private readonly bool uninstall;
    private readonly string source;
    private readonly Func<string,string[],IProgress<MaintenanceProgress>,Task> runner;
    private readonly StackPanel body=new();
    private readonly Button primary=new(){Padding=new Thickness(18,11,18,11),MinWidth=120,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(0,122,255))};
    private readonly Button secondary=new(){Padding=new Thickness(16,11,16,11),MinWidth=86,Margin=new Thickness(0,0,10,0)};
    private readonly TextBox location=new(){Padding=new Thickness(10),FontSize=12,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    private readonly TextBlock stage=new(){FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,20,0,8)};
    private readonly ProgressBar progress=new(){Height=5,Minimum=0,Maximum=100};
    private bool busy;
    internal MaintenanceState State{get;private set;}
    internal void BeginPreparation(string caption="准备卸载界面…"){busy=true;ShowState(MaintenanceState.Working);stage.Text=caption;progress.IsIndeterminate=true;}
    internal void EndPreparation(){busy=false;}
    internal MaintenanceWindow(bool uninstall=false,string? directory=null,Func<string,string[],IProgress<MaintenanceProgress>,Task>? runner=null)
    {
        this.uninstall=uninstall;source=directory ?? AppContext.BaseDirectory;this.runner=runner ?? MaintenanceProcess.RunAsync;
        Title=uninstall ? "卸载灵动胶囊" : "安装灵动胶囊";Width=Math.Min(540,SystemParameters.WorkArea.Width-32);Height=Math.Min(520,SystemParameters.WorkArea.Height-32);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=Design.UserFont;FontSize=13;Design.Bind(this,ForegroundProperty,"TextPrimary");
        var layout=new Grid{Margin=new Thickness(16)};layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,24)};brand.Children.Add(new Image{Source=new BitmapImage(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.png")),Width=44,Height=44,Margin=new Thickness(0,0,12,0)});brand.Children.Add(new TextBlock{Text="灵动胶囊",FontSize=20,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});layout.Children.Add(brand);
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};actions.Children.Add(secondary);actions.Children.Add(primary);Grid.SetRow(actions,2);layout.Children.Add(actions);Content=Design.Card(layout);
        SizeChanged+=(_,_)=>{var narrow=ActualWidth<350;var shortWindow=ActualHeight<300;primary.MinWidth=narrow ? 88 : 120;secondary.MinWidth=narrow ? 58 : 86;layout.Margin=new Thickness(shortWindow ? 8 : 16);brand.Margin=new Thickness(0,0,0,shortWindow ? 12 : 24);actions.Margin=new Thickness(0,shortWindow ? 12 : 22,0,0);};
        Design.Bind(location,TextBox.ForegroundProperty,"TextPrimary");
        location.Text=uninstall ? source : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LingdongCapsule");
        primary.Click+=async(_,_)=>await NextAsync();secondary.Click+=(_,_)=>{if(busy)return;if(State==MaintenanceState.Location)ShowState(MaintenanceState.Welcome);else Close();};Closing+=(_,args)=>{if(busy)args.Cancel=true;};
        PreviewKeyDown+=(_,args)=>{if(args.Key==System.Windows.Input.Key.Escape && !busy){Close();args.Handled=true;}};Loaded+=(_,_)=>BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));ShowState(MaintenanceState.Welcome);
    }
    private void Text(string text,double size=13,bool muted=true)
    {var label=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,LineHeight=size+8,Margin=new Thickness(0,0,0,12),FontWeight=muted ? FontWeights.Normal : FontWeights.SemiBold};Design.Bind(label,TextBlock.ForegroundProperty,muted ? "TextSecondary" : "TextPrimary");body.Children.Add(label);}
    private void ShowState(MaintenanceState state,string error="")
    {
        State=state;body.Children.Clear();primary.IsEnabled=secondary.IsEnabled=!busy;secondary.Visibility=Visibility.Visible;
        switch(state)
        {
            case MaintenanceState.Welcome:
                Text(uninstall ? "准备卸载灵动胶囊？" : "让重要的信息，轻轻出现。",25,false);Text(uninstall ? "将移除程序文件、快捷方式及本程序的启动登记。\n个人设置会保留，重新安装后可以继续使用。\n不会删除其他应用和不属于胶囊的文件。" : "把媒体、通知与日常信息收在屏幕顶部。\n安装到当前用户，无需管理员权限。\n安装后可通过引导完成设置，日常从托盘管理。");primary.Content=uninstall ? "确认卸载" : "开始安装";secondary.Content="取消";break;
            case MaintenanceState.Location:
                Text("选择安装位置",25,false);Text("升级保留个人设置；已有其他文件的目录不会被直接覆盖。");Text("安装目录",12);
                if(location.Parent is Border previous)previous.Child=null;
                var field=new Border{CornerRadius=new CornerRadius(10),Child=location};Design.Bind(field,Border.BackgroundProperty,"ButtonFill");body.Children.Add(field);
                var browse=new Button{Content="更改位置…",Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0)};browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title="选择安装目录"};if(picker.ShowDialog(this)==true)location.Text=Path.Combine(picker.FolderName,"LingdongCapsule");};body.Children.Add(browse);primary.Content="安装";secondary.Content="返回";break;
            case MaintenanceState.Working:
                Text(uninstall ? "正在卸载" : "正在安装",25,false);Text("请稍候，完成后会在这里显示结果。");body.Children.Add(stage);body.Children.Add(progress);primary.Content="处理中…";secondary.Content="请稍候";break;
            case MaintenanceState.Completed:
                Text(uninstall ? "卸载完成" : "安装完成",25,false);Text(uninstall ? "程序已移除，个人设置已保留。\n感谢你使用灵动胶囊。" : "灵动胶囊已准备就绪。\n打开后可通过引导完成设置，日常从系统托盘查看与管理。");primary.Content=uninstall ? "完成" : "打开灵动胶囊";secondary.Content="关闭";if(uninstall)secondary.Visibility=Visibility.Collapsed;break;
            case MaintenanceState.Failed:
                Text(uninstall ? "卸载未完成" : "安装未完成",25,false);Text("个人设置会保留，请检查原因后重试。");Text(error,12);primary.Content="重试";secondary.Content="关闭";break;
        }
        body.BeginAnimation(OpacityProperty,new DoubleAnimation(.6,1,TimeSpan.FromMilliseconds(140)));
    }
    private async Task NextAsync()
    {
        if(busy)return;
        if(State==MaintenanceState.Welcome && !uninstall){ShowState(MaintenanceState.Location);return;}
        if(State==MaintenanceState.Completed){if(!uninstall)try{Process.Start(new ProcessStartInfo(Path.Combine(location.Text,"LingdongCapsule.exe")){UseShellExecute=true});}catch(Exception error){Design.PresentMessage("未能打开程序",error.Message);return;}Close();return;}
        try
        {
            var target=Path.GetFullPath(location.Text.Trim());if(!Path.IsPathFullyQualified(location.Text.Trim()) || target==Path.GetPathRoot(target))throw new IOException("请选择有效的程序安装目录。");
            busy=true;ShowState(MaintenanceState.Working);stage.Text="准备文件…";progress.IsIndeterminate=true;
            var updates=new Progress<MaintenanceProgress>(update=>{if(!busy)return;stage.Text=update.Stage switch{"preparing"=>"准备文件","copying"=>"复制并校验文件","committing"=>"更新程序文件","registering"=>"创建应用入口","stopping"=>"关闭本程序","removing"=>"移除程序文件","completed"=>"操作完成",_=>"正在处理"};progress.IsIndeterminate=update.Total==0;if(update.Total>0)progress.Value=100d*update.Completed/update.Total;});
            await runner(Path.Combine(source,uninstall ? "Uninstall.ps1" : "Install.ps1"),uninstall ? ["-ReportProgress"] : ["-InstallDir",target,"-NoLaunch","-ReportProgress"],updates);
            busy=false;ShowState(MaintenanceState.Completed);
        }
        catch(Exception error){busy=false;ShowState(MaintenanceState.Failed,error.Message);}
    }
}
