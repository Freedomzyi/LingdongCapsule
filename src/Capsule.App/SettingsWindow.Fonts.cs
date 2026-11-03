using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Capsule.App;

public sealed partial class SettingsWindow
{
    private void AddFontSettings(StackPanel panel,Action persist,Func<string?>? picker)
    {
        Design.Bind(this,FontFamilyProperty,"UserFont");
        var card=new StackPanel();card.Children.Add(Label("字体","选择已安装字体或本地 TTF/OTF/TTC。只保存路径，不安装、复制或分发字体；缺少字形由系统回退。"));
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,8,0,4)};
        var current=new Button{MaxWidth=220,Padding=new(10,7,10,7)};
        var file=new Button{Content="选择字体文件…",Padding=new(10,7,10,7),Margin=new(8,0,0,0)};
        System.Windows.Automation.AutomationProperties.SetName(current,"字体");System.Windows.Automation.AutomationProperties.SetName(file,"选择字体文件");
        controls.Children.Add(current);controls.Children.Add(file);card.Children.Add(controls);
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,5,0,0)};Design.Bind(feedback,TextBlock.ForegroundProperty,"TextSecondary");card.Children.Add(feedback);
        void Paint()
        {
            current.Content=new TextBlock{Text=(settings.FontFilePath.Length>0 ? Path.GetFileName(settings.FontFilePath) : settings.FontName.Length>0 ? settings.FontName : "系统默认")+" ▾",TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=195};
            current.ToolTip=settings.FontFilePath.Length>0 ? settings.FontFilePath : "选择字体，或恢复系统默认";feedback.Text=Design.FontStatus;
        }
        void Change(string name,string path)
        {
            var oldName=settings.FontName;var oldPath=settings.FontFilePath;
            try
            {
                if(path.Length>0)LocalFont.Load(path);
                settings.FontName=name;settings.FontFilePath=path;
                persist();Design.ApplyFont(name,path);Paint();
            }
            catch(Exception error) when(LocalFont.IsLoadError(error))
            {
                settings.FontName=oldName;settings.FontFilePath=oldPath;Design.ApplyFont(oldName,oldPath);Paint();feedback.Text="字体未能更新："+error.Message;
            }
        }
        current.Click+=(_,_)=>
        {
            var menu=new ContextMenu{PlacementTarget=current};current.ContextMenu=menu;
            var reset=new MenuItem{Header="系统默认（恢复默认）"};reset.Click+=(_,_)=>Change("","");menu.Items.Add(reset);
            foreach(var font in Fonts.SystemFontFamilies.OrderBy(font=>font.Source))
            {var name=font.Source;var item=new MenuItem{Header=name,IsCheckable=true,IsChecked=settings.FontFilePath.Length==0 && settings.FontName==name};item.Click+=(_,_)=>Change(name,"");menu.Items.Add(item);}
            menu.IsOpen=true;
        };
        file.Click+=(_,_)=>
        {
            try
            {
                var selected=picker!=null ? picker() : Pick();if(selected!=null)Change("",Path.GetFullPath(selected));
            }
            catch(Exception error) when(LocalFont.IsLoadError(error)){feedback.Text="字体未能打开："+error.Message;}
        };
        string? Pick(){var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择灵动胶囊字体文件",Filter="字体文件|*.ttf;*.otf;*.ttc",CheckFileExists=true};return dialog.ShowDialog(this)==true ? dialog.FileName : null;}
        refreshPreferences.Add(Paint);Paint();panel.Children.Add(Design.Card(card));
    }
}
