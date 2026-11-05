using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal sealed class OverviewView : Grid,IDisposable
{
    private readonly Settings settings;
    private readonly Action save;
    private readonly OverviewService sampler=new();
    private readonly WeatherService weather;
    private readonly TextBlock heading=Text(16),today=Text(12),tomorrow=Text(12,true),note=Text(8,true);
    private readonly TextBlock download=Text(11),upload=Text(11),network=Text(11),ram=Text(11),cpu=Text(11),battery=Text(11);
    private readonly StackPanel processes=new();
    private readonly Dictionary<string,ImageSource?> icons=[];
    private long nextProcesses;
    private bool reading,disposed;
    internal OverviewSample? CurrentSample{get;private set;}
    internal IReadOnlyList<MemoryGroup> CurrentProcesses{get;private set;}=[];
    internal WeatherData? CurrentWeather=>weather.Data;
    internal bool DialogOpen{get;private set;}
    internal bool ContentFits=>Children[0] is FrameworkElement content && content.ActualHeight<=ActualHeight+0.5;
    internal string LayoutSizes=>$"available={ActualHeight:0.##}; content={((FrameworkElement)Children[0]).ActualHeight:0.##}";
    public OverviewView(Settings settings,Action save,WeatherService? weatherService=null)
    {
        this.settings=settings;this.save=save;weather=weatherService ?? new();
        var root=new StackPanel();Children.Add(root);
        var weatherContent=new StackPanel();weatherContent.Children.Add(heading);weatherContent.Children.Add(today);weatherContent.Children.Add(tomorrow);weatherContent.Children.Add(note);
        var edit=new Button{Content=weatherContent,Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(0,0,0,3),ToolTip="修改天气城市"};
        edit.Click+=(_,eventArgs)=>{eventArgs.Handled=true;EditCity();};root.Children.Add(edit);
        var columns=new Grid{Margin=new Thickness(0,3,0,0)};columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new());
        var net=Tile("实时网速",new[]{("下载",download),("上传",upload),("状态",network)});net.Margin=new Thickness(0,0,3,0);columns.Children.Add(net);
        var hardware=Tile("运行状态",new[]{("内存",ram),("CPU",cpu),("电池",battery)});hardware.Margin=new Thickness(3,0,0,0);Grid.SetColumn(hardware,1);columns.Children.Add(hardware);root.Children.Add(columns);
        var label=Text(11,true);label.Text="内存排行";label.Margin=new Thickness(0,4,0,5);root.Children.Add(label);
        var ranking=new Border{CornerRadius=new CornerRadius(12),Child=processes};Design.Bind(ranking,Border.BackgroundProperty,"OverviewTile");root.Children.Add(ranking);
        weather.Changed+=RenderWeather;weather.SelectCity(settings.WeatherCity);RenderWeather();
        MouseLeftButtonUp+=(_,args)=>args.Handled=true;
    }
    private static TextBlock Text(double size,bool muted=false)
    {
        var text=new TextBlock{FontSize=size,HorizontalAlignment=HorizontalAlignment.Stretch,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};Design.Bind(text,TextBlock.ForegroundProperty,muted ? "TextSecondary" : "IslandInk");return text;
    }
    private static Border Tile(string title,(string Label,TextBlock Value)[] rows)
    {
        var content=new StackPanel{Margin=new Thickness(8,5,8,5)};var caption=Text(13);caption.Text=title;caption.Height=18;caption.Margin=new Thickness(0,0,0,2);content.Children.Add(caption);
        foreach(var row in rows)
        {
            var line=new Grid{Height=19};line.ColumnDefinitions.Add(new(){Width=GridLength.Auto});line.ColumnDefinitions.Add(new());
            var name=Text(10,true);name.Text=row.Label;line.Children.Add(name);row.Value.Text="—";row.Value.TextAlignment=TextAlignment.Right;Grid.SetColumn(row.Value,1);line.Children.Add(row.Value);content.Children.Add(line);
        }
        var tile=new Border{CornerRadius=new CornerRadius(11),Child=content};Design.Bind(tile,Border.BackgroundProperty,"OverviewTile");return tile;
    }
    internal void EditCity()
    {
        DialogOpen=true;
        try
        {
            var selected=CardDialog.Show("天气城市","输入城市名，保存后更新天气。","保存",settings.WeatherCity);
            if(selected==null || selected==settings.WeatherCity)return;
            var previous=settings.WeatherCity;settings.WeatherCity=selected;
            try{save();}
            catch(Exception error){settings.WeatherCity=previous;Design.PresentMessage("天气城市未保存",error.Message);return;}
            weather.SelectCity(selected);_=weather.RefreshIfDueAsync();
        }
        finally{DialogOpen=false;}
    }
    internal async Task TickAsync()
    {
        if(disposed || !settings.OverviewEnabled)return;
        weather.SelectCity(settings.WeatherCity);RenderWeather();_=weather.RefreshIfDueAsync();
        var sample=sampler.Sample();CurrentSample=sample;
        ram.Text=sample.Memory is {} memory ? $"{memory}%" : "—";cpu.Text=sample.Cpu is {} cpuValue ? $"{cpuValue:0}%" : "—";battery.Text=sample.Battery;
        if(sample.LowBattery)battery.Foreground=new SolidColorBrush(Color.FromRgb(237,151,65));else Design.Bind(battery,TextBlock.ForegroundProperty,"IslandInk");
        download.Text=Rate(sample.Download);upload.Text=Rate(sample.Upload);network.Text=sample.Network;
        network.Foreground=new SolidColorBrush(sample.Network=="健康" ? Color.FromRgb(72,189,127) : sample.Network=="欠佳" ? Color.FromRgb(237,151,65) : Color.FromRgb(164,164,174));
        if(reading || Environment.TickCount64<nextProcesses)return;
        reading=true;
        try
        {
            var ranked=await Task.Run(sampler.ReadProcesses);
            if(disposed || !settings.OverviewEnabled)return;
            CurrentProcesses=ranked;RenderProcesses();
        }
        finally{reading=false;nextProcesses=Environment.TickCount64+5000;}
    }
    private static string Rate(double? bytes)=>bytes is not {} value ? "—" : value>=1048576 ? $"{value/1048576:0.0} MB/s" : $"{value/1024:0} KB/s";
    private void RenderWeather()
    {
        if(disposed)return;
        heading.Text=weather.Data is {} data ? $"{data.City}  {data.Temperature:0.#}°" : weather.Status;
        today.Text=weather.Data?.Today.Display(false) ?? "今日 · 暂无预报";tomorrow.Text=weather.Data?.Tomorrow.Display(true) ?? "明日 · 暂无预报";
        tomorrow.Visibility=settings.ShowTomorrow ? Visibility.Visible : Visibility.Collapsed;
        today.Height=18;tomorrow.Height=18;heading.Height=22;
        note.Text=weather.Data!=null ? weather.Status : "";note.Height=10;
        // Attribution stays discoverable without changing the reference layout.
        heading.ToolTip="天气数据：Open-Meteo · CC BY 4.0\nhttps://open-meteo.com/";
    }
    private void RenderProcesses()
    {
        processes.Children.Clear();
        foreach(var stale in icons.Keys.Except(CurrentProcesses.Select(group=>group.Name)).ToArray())icons.Remove(stale);
        foreach(var group in CurrentProcesses)
        {
            var row=new Grid{Height=25,Margin=new Thickness(8,0,4,0)};row.ColumnDefinitions.Add(new(){Width=new GridLength(21)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(25)});
            if(!icons.TryGetValue(group.Name,out var source))
            {
                source=null;
                try
                {
                    if(group.IconPath!=null)
                    {
                        using var icon=System.Drawing.Icon.ExtractAssociatedIcon(group.IconPath);using var bitmap=icon?.ToBitmap();
                        if(bitmap!=null){using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;var decoded=new BitmapImage();decoded.BeginInit();decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.StreamSource=stream;decoded.DecodePixelWidth=32;decoded.EndInit();decoded.Freeze();source=decoded;}
                    }
                }
                catch(Exception error) when(error is IOException or ArgumentException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){ }
                icons[group.Name]=source;
            }
            if(source!=null)row.Children.Add(new Image{Source=source,Width=15,Height=15});else{var fallback=Text(10,true);fallback.Text=group.Name[..1];row.Children.Add(fallback);}
            var name=Text(12);name.Text=group.Name.Equals("Weixin",StringComparison.OrdinalIgnoreCase) ? "微信" : group.Name;name.TextAlignment=TextAlignment.Left;Grid.SetColumn(name,1);row.Children.Add(name);
            var amount=Text(11,true);amount.Text=group.Bytes>=1073741824 ? $"{group.Bytes/1073741824d:0.0} GB" : $"{group.Bytes/1048576d:0} MB";Grid.SetColumn(amount,2);row.Children.Add(amount);
            var end=new Button{Content="×",Background=Brushes.Transparent,Foreground=new SolidColorBrush(Color.FromRgb(164,164,174)),FontSize=15,ToolTip="结束此进程组",IsEnabled=!group.Processes.Any(process=>process.Id==Environment.ProcessId)};
            Design.Bind(end,Control.ForegroundProperty,"TextSecondary");Grid.SetColumn(end,3);end.Click+=(_,args)=>{args.Handled=true;ConfirmEnd(group);};row.Children.Add(end);processes.Children.Add(row);
        }
        if(CurrentProcesses.Count==0){var empty=Text(12,true);empty.Text="暂无可读取的进程";empty.Height=75;processes.Children.Add(empty);}
    }
    private void ConfirmEnd(MemoryGroup group)
    {
        var wechat=group.Name.Equals("Weixin",StringComparison.OrdinalIgnoreCase);
        var detail=wechat ? "微信\n将结束微信主程序及其子进程，未保存内容可能丢失。" : $"{group.Name} · {group.Processes.Count} 个同名进程\n将强制结束这些进程，未保存内容可能丢失。共享组件可能影响多个应用。";
        string? confirmed;DialogOpen=true;
        try{confirmed=CardDialog.Show("关闭应用程序？",detail,"结束进程",destructive:true);}finally{DialogOpen=false;}
        if(confirmed==null)return;
        var failures=OverviewService.EndConfirmed(group);nextProcesses=0;
        if(failures>0)Design.PresentMessage("部分进程未能结束",$"{failures} 个进程无法结束，请在任务管理器中检查。");
        _=TickAsync();
    }
    public void Dispose(){disposed=true;weather.Dispose();icons.Clear();}
    internal void SetEnabled(bool enabled){if(!enabled)weather.Suspend();}
}
