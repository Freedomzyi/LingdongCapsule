using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Capsule.App;

internal sealed class PluginMarketWindow : Window
{
    private readonly HttpClient http;
    private readonly PluginMarket market;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Action<string> install;
    private readonly Func<PluginCatalogEntry[]> installed;
    private readonly Action changed;
    private readonly StackPanel rows=new();
    private readonly TextBlock status=new(){Text="尚未刷新目录",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,10,0,10)};
    private readonly TextBox search=new(){Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(10,8,10,8),ToolTip="搜索名称、作者或描述",MaxLength=256};
    private readonly Button refresh=new(){Content="刷新目录",Padding=new(12,8,12,8)};
    private readonly Button categoryButton=new(){Content="全部类别",Padding=new(12,8,12,8),Margin=new(8,0,0,0)};
    private readonly CheckBox updates=new(){Content="只看更新",Margin=new(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    private MarketPlugin[] packages=[];
    private string category="";
    private bool newest,working,closed;
    internal PluginMarketWindow(Action<string> install,Func<PluginCatalogEntry[]> installed,Action changed,HttpClient? testHttp=null)
    {
        this.install=install;this.installed=installed;this.changed=changed;
        http=testHttp ?? new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(25)};market=new(http);
        Title="灵动胶囊 · 插件市场";Width=Math.Min(660,SystemParameters.WorkArea.Width-40);Height=Math.Min(700,SystemParameters.WorkArea.Height-48);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=Design.UserFont;FontSize=13;
        Design.Bind(this,ForegroundProperty,"TextPrimary");
        var outer=new Border{CornerRadius=new(22),BorderThickness=new(1),BorderBrush=new SolidColorBrush(Color.FromArgb(35,128,128,128)),Padding=new(22,18,22,16)};Design.Bind(outer,Border.BackgroundProperty,"PageFill");Content=outer;
        var layout=new Grid();layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new());outer.Child=layout;
        var header=new DockPanel{Margin=new(0,0,0,16)};var close=new Button{Content="×",Width=30,Height=30,FontSize=20,ToolTip="关闭市场"};DockPanel.SetDock(close,Dock.Right);close.Click+=(_,_)=>Close();header.Children.Add(close);header.Children.Add(new TextBlock{Text="插件市场",FontSize=24,FontWeight=FontWeights.SemiBold});header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is not Button && e.LeftButton==MouseButtonState.Pressed)DragMove();};layout.Children.Add(header);
        var controls=new StackPanel();Grid.SetRow(controls,1);layout.Children.Add(controls);
        var searchLayout=new Grid();searchLayout.Children.Add(search);var placeholder=new TextBlock{Text="搜索组件、作者或描述",Margin=new(11,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};Design.Bind(placeholder,TextBlock.ForegroundProperty,"TextSecondary");searchLayout.Children.Add(placeholder);
        var searchBorder=new Border{CornerRadius=new(12),Child=searchLayout,Margin=new(0,0,0,10)};Design.Bind(searchBorder,Border.BackgroundProperty,"CardFill");Design.Bind(search,ForegroundProperty,"TextPrimary");controls.Children.Add(searchBorder);
        var toolbar=new WrapPanel();toolbar.Children.Add(refresh);toolbar.Children.Add(categoryButton);var sorting=new Button{Content="按名称",Padding=new(12,8,12,8),Margin=new(8,0,0,0)};toolbar.Children.Add(sorting);toolbar.Children.Add(updates);controls.Children.Add(toolbar);
        Grid.SetRow(status,2);Design.Bind(status,TextBlock.ForegroundProperty,"TextSecondary");layout.Children.Add(status);
        var scroll=new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,3);layout.Children.Add(scroll);
        search.TextChanged+=(_,_)=>{placeholder.Visibility=search.Text.Length==0 ? Visibility.Visible : Visibility.Collapsed;Render();};updates.Checked+=(_,_)=>Render();updates.Unchecked+=(_,_)=>Render();sorting.Click+=(_,_)=>{newest=!newest;sorting.Content=newest ? "按版本" : "按名称";Render();};
        categoryButton.Click+=(_,_)=>
        {
            var menu=new ContextMenu{PlacementTarget=categoryButton};
            foreach(var name in new[]{""}.Concat(packages.Select(item=>item.Category).Distinct().Order())){var value=name;var option=new MenuItem{Header=value.Length==0 ? "全部类别" : value,IsCheckable=true,IsChecked=category==value};option.Click+=(_,_)=>{category=value;categoryButton.Content=value.Length==0 ? "全部类别" : value;Render();};menu.Items.Add(option);}menu.IsOpen=true;
        };
        refresh.Click+=async(_,_)=>await RefreshAsync();Loaded+=async(_,_)=>await RefreshAsync();
        Closed+=(_,_)=>{closed=true;lifetime.Cancel();http.Dispose();lifetime.Dispose();};
    }
    internal async Task RefreshAsync()
    {
        if(working || closed)return;working=true;refresh.IsEnabled=false;status.Text="正在读取官方目录…";Render();
        try{packages=await market.ReadAsync(lifetime.Token);if(closed)return;status.Text=packages.Length==0 ? "官方目录暂未上架组件；可从“我的插件”安装随程序提供的本地包。" : $"官方目录 · {packages.Length} 个组件";}
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or IOException or System.Text.Json.JsonException or InvalidOperationException){if(!closed)status.Text="目录未能刷新，保留本次已有列表。请检查网络或稍后重试。";}
        finally{working=false;if(!closed){refresh.IsEnabled=true;Render();}}
    }
    private void Render()
    {
        rows.Children.Clear();var local=installed();var query=search.Text.Trim();
        IEnumerable<MarketPlugin> visible=packages.Where(item=>(category.Length==0 || item.Category==category) && (query.Length==0 || (item.Name+" "+item.Author+" "+item.Description).Contains(query,StringComparison.CurrentCultureIgnoreCase)));
        visible=newest ? visible.OrderByDescending(item=>PluginMarket.VersionOf(item.Version)) : visible.OrderBy(item=>item.Name);
        foreach(var item in visible)
        {
            var copies=local.Where(entry=>entry.Id==item.Id && entry.Error.Length==0 && !entry.PendingRemoval).OrderByDescending(entry=>PluginMarket.VersionOf(entry.Version)).ToArray();
            var best=copies.FirstOrDefault();var update=best!=null && PluginMarket.VersionOf(item.Version)>PluginMarket.VersionOf(best.Version);
            if(updates.IsChecked==true && !update)continue;
            var same=copies.FirstOrDefault(entry=>PluginMarket.VersionOf(entry.Version)==PluginMarket.VersionOf(item.Version));var host=typeof(MainWindow).Assembly.GetName().Version!;var compatible=PluginMarket.VersionOf(item.MinimumHostVersion)<=host;
            var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=item.Name+" · "+item.Version,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});panel.Children.Add(new TextBlock{Text=item.Description,TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,5)});
            var attribution=new TextBlock{Text=item.Author+" · "+item.License+" · "+item.Category,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};Design.Bind(attribution,TextBlock.ForegroundProperty,"TextSecondary");panel.Children.Add(attribution);
            var higher=best!=null && PluginMarket.VersionOf(best.Version)>PluginMarket.VersionOf(item.Version);
            var action=new Button{Content=!compatible ? "需更新主程序" : same?.Enabled==true ? "已启用" : same!=null ? "启用" : higher ? "已安装更高版本" : update ? "更新" : "安装",HorizontalAlignment=HorizontalAlignment.Left,Padding=new(14,8,14,8),IsEnabled=!working && compatible && same?.Enabled!=true && !higher};
            action.Click+=async(_,_)=>await InstallAsync(item,same?.EntryFile);panel.Children.Add(action);rows.Children.Add(Design.Card(panel));
        }
        if(rows.Children.Count==0 && packages.Length>0)rows.Children.Add(new TextBlock{Text="没有符合条件的组件。",Margin=new(0,12,0,0)});
    }
    private async Task InstallAsync(MarketPlugin package,string? existing)
    {
        if(working || closed)return;
        if(CardDialog.Show("安装并运行此组件？",$"{package.Name} · {package.Author}\n许可：{package.License}\n插件与主程序同进程运行，可访问本机资源。请仅安装信任的组件。","继续")==null)return;
        working=true;refresh.IsEnabled=false;status.Text=existing==null ? "正在下载并校验…" : "正在启用…";Render();string? download=null;
        try
        {
            download=existing==null ? await market.DownloadAsync(package,Path.Combine(Settings.Folder,"Downloads"),typeof(MainWindow).Assembly.GetName().Version!,lifetime.Token) : null;
            if(closed)return;install(existing ?? download!);changed();status.Text="组件已启用。";
        }
        catch(Exception error)
        {if(!closed)status.Text="安装未完成："+error.Message;}
        finally
        {
            if(download!=null)try{File.Delete(download);}catch(IOException){ }catch(UnauthorizedAccessException){ }
            working=false;if(!closed){refresh.IsEnabled=true;Render();}
        }
    }
}
