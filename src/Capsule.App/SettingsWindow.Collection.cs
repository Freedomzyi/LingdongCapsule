using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

internal sealed record AppearancePreset(string Id,string Name,string Description,int Theme,int Material,double Opacity)
{
    internal static readonly AppearancePreset[] All=
    [new("obsidian","曜石","沉静深色 · 实体",0,0,98),new("moonlight","月光","柔和浅色 · 实体",1,0,98),new("glass","流光玻璃","通透深色 · 液态玻璃",0,2,86)];
    internal bool Matches(Settings settings)=>settings.ThemeMode==Theme && settings.MaterialKind==Material && Math.Abs(settings.BackgroundOpacity-Opacity)<.01;
    internal void Apply(Settings settings){settings.ThemeMode=Theme;settings.MaterialKind=Material;settings.BackgroundOpacity=Opacity;}
}

internal sealed class CollectionGlyph(bool timer) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawing)
    {
        var pen=new Pen(new SolidColorBrush(Color.FromRgb(22,49,57)),1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        drawing.PushTransform(new ScaleTransform(ActualWidth/30,ActualHeight/30));
        if(timer){drawing.DrawEllipse(null,pen,new Point(15,17),10,10);drawing.DrawLine(pen,new Point(15,7),new Point(15,3));drawing.DrawLine(pen,new Point(11,3),new Point(19,3));drawing.DrawLine(pen,new Point(15,17),new Point(15,11));drawing.DrawLine(pen,new Point(15,17),new Point(19,19));}
        else{drawing.DrawRoundedRectangle(null,pen,new Rect(4,6,22,21),4,4);drawing.DrawLine(pen,new Point(4,12),new Point(26,12));drawing.DrawLine(pen,new Point(10,3),new Point(10,9));drawing.DrawLine(pen,new Point(20,3),new Point(20,9));drawing.DrawLine(pen,new Point(9,18),new Point(13,18));drawing.DrawLine(pen,new Point(17,18),new Point(21,18));drawing.DrawLine(pen,new Point(9,22),new Point(13,22));}
        drawing.Pop();
    }
}

public sealed partial class SettingsWindow
{
    private static readonly HashSet<string> BundledComponentIds=["capsule.focustimer","capsule.dayprogress"];
    private Action refreshThemeCards=()=>{};
    private void AddThemePresets(StackPanel panel)
    {
        Heading(panel,"精选主题");
        var grid=new Grid();foreach(var _ in AppearancePreset.All)grid.ColumnDefinitions.Add(new());
        var buttons=new List<(AppearancePreset Preset,Button Button,TextBlock State)>();
        foreach(var preset in AppearancePreset.All)
        {
            var contents=new StackPanel{Width=120};
            var preview=new Grid{Height=70,ClipToBounds=true};
            var backdrop=new Border{CornerRadius=new CornerRadius(10),Background=new LinearGradientBrush(preset.Material==2 ? Color.FromRgb(111,97,174) : preset.Theme==1 ? Color.FromRgb(218,228,240) : Color.FromRgb(42,44,55),preset.Theme==1 ? Color.FromRgb(244,237,228) : Color.FromRgb(25,29,47),45)};preview.Children.Add(backdrop);
            var mini=new Border{CornerRadius=new CornerRadius(15),Height=29,Margin=new Thickness(10,0,10,0),Background=new SolidColorBrush(preset.Theme==1 ? Color.FromRgb(248,248,250) : preset.Material==2 ? Color.FromArgb(150,35,34,49) : Color.FromRgb(16,16,18)),BorderBrush=new SolidColorBrush(Color.FromArgb(60,150,150,170)),BorderThickness=new Thickness(1)};
            mini.Child=new TextBlock{Text="09:41",FontSize=13,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=preset.Theme==1 ? Brushes.Black : Brushes.White};preview.Children.Add(mini);contents.Children.Add(preview);
            var title=new TextBlock{Text=preset.Name,FontWeight=FontWeights.SemiBold,FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,9,0,3)};Design.Bind(title,TextBlock.ForegroundProperty,"TextPrimary");contents.Children.Add(title);
            var state=new TextBlock{FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis};Design.Bind(state,TextBlock.ForegroundProperty,"TextSecondary");contents.Children.Add(state);
            var button=new Button{Content=contents,Padding=new Thickness(9),Margin=new Thickness(3),HorizontalContentAlignment=HorizontalAlignment.Stretch,Tag="theme:"+preset.Id,ToolTip=preset.Description+"；只调整主题、材质和透明度，保留字体与布局。"};
            System.Windows.Automation.AutomationProperties.SetName(button,"应用"+preset.Name+"主题");
            button.Click+=(_,_)=>
            {
                var old=(settings.ThemeMode,settings.MaterialKind,settings.BackgroundOpacity);
                if(CommitChange(()=>preset.Apply(settings),()=>{settings.ThemeMode=old.ThemeMode;settings.MaterialKind=old.MaterialKind;settings.BackgroundOpacity=old.BackgroundOpacity;}))
                {Design.ApplyTheme(settings.ThemeMode);RefreshPreferences();}
            };
            Grid.SetColumn(button,buttons.Count);grid.Children.Add(button);buttons.Add((preset,button,state));
        }
        grid.SizeChanged+=(_,_)=>{foreach(var item in buttons)if(item.Button.Content is FrameworkElement content)content.Width=Math.Max(0,grid.ActualWidth/3-26);};
        void Refresh(){foreach(var item in buttons){var active=item.Preset.Matches(settings);item.State.Text=active ? "✓ 正在使用" : item.Preset.Description;item.Button.BorderBrush=active ? new SolidColorBrush(Color.FromRgb(0,122,255)) : Brushes.Transparent;item.Button.BorderThickness=new Thickness(active ? 1.5 : 0);}}
        refreshThemeCards=Refresh;refreshPreferences.Add(Refresh);Refresh();panel.Children.Add(grid);
    }
    private static Action AddBundledComponents(StackPanel panel,Action<string> load,Action<string> disable,Func<string[]> active,Func<PluginCatalogEntry[]> installed,Action changed,string? directory=null)
    {
        directory??=Path.Combine(AppContext.BaseDirectory,"plugins");
        Heading(panel,"精选组件");
        var entries=new[]{("capsule.focustimer","专注计时","25 / 45 分钟专注与短休息","#5FD2A5"),("capsule.dayprogress","今日进度","日期、星期与今日已过百分比","#64A6FF")};
        var refreshers=new List<Action>();
        foreach(var (id,name,description,color) in entries)
        {
            var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(68)});content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var preview=new Border{Width=54,Height=54,CornerRadius=new CornerRadius(16),Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))};preview.Child=new CollectionGlyph(id=="capsule.focustimer"){Width=30,Height=30,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};content.Children.Add(preview);
            var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=new TextBlock{Text=name,FontSize=14,FontWeight=FontWeights.SemiBold};Design.Bind(title,TextBlock.ForegroundProperty,"TextPrimary");labels.Children.Add(title);var detail=new TextBlock{Text=description,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,6,0)};Design.Bind(detail,TextBlock.ForegroundProperty,"TextSecondary");labels.Children.Add(detail);Grid.SetColumn(labels,1);content.Children.Add(labels);
            var add=new Button{Content="添加",Padding=new Thickness(12,8,12,8),VerticalAlignment=VerticalAlignment.Center,Tag="bundled:"+id};System.Windows.Automation.AutomationProperties.SetName(add,"添加"+name);Grid.SetColumn(add,2);content.Children.Add(add);
            var archive=Path.Combine(directory,id+"-0.1.0.zip");
            void Refresh(){var enabled=active().Contains(id);var entry=installed().FirstOrDefault(item=>item.Id==id && item.CanEnable);add.Content=enabled ? "停用" : entry!=null ? "启用" : File.Exists(archive) ? "添加" : "未附带";add.IsEnabled=enabled || entry!=null || File.Exists(archive);System.Windows.Automation.AutomationProperties.SetName(add,(enabled ? "停用" : "添加或启用")+name);}
            add.Click+=(_,_)=>{try{if(active().Contains(id))disable(id);else{var entry=installed().FirstOrDefault(item=>item.Id==id && item.CanEnable);load(entry?.EntryFile ?? archive);}changed();Refresh();}catch(Exception error){Design.PresentMessage("组件未能更新",error.Message);Refresh();}};
            refreshers.Add(Refresh);panel.Children.Add(Design.Card(content));
        }
        void Update(){foreach(var refresh in refreshers)refresh();}Update();return Update;
    }
}
