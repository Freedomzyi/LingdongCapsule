using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capsule.Core;

namespace Capsule.App;

public partial class MainWindow
{
    private Point? noticePress;
    private Notice? pressedNotice;
    private bool noticeSwiped;
    private readonly NotificationSound notificationSound=new();
    private readonly NoticeImages noticeImages=new();
    private CancellationTokenSource noticeImageRequest=new();
    internal int NoticeSoundPlayCount=>notificationSound.PlayCount;
    private readonly BitmapImage defaultNoticeIcon=new(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.png"));
    internal bool AcceptNotification(Notice notice,DateTimeOffset now)
    {
        if(!notifications.Sources.ContainsKey(notice.SourceId) && notifications.Sources.Count<256){notifications.Sources[notice.SourceId]=notice.App;notifications.PersistSourceNames();}
        state.GroupNotifications=settings.GroupNotifications;
        state.SetQuietUntil(settings.QuietUntil,now);
        if(!settings.NotificationsEnabled || settings.MutedSources.Contains(notice.SourceId) || !state.Show(notice,now))return false;
        if(!state.LastNoticeGrouped)_=notificationSound.PlayAsync(settings);
        controlsOpen=false;overviewOpen=false;Render();_=ResolveNoticeImagesAsync(notice);return true;
    }
    private async Task ResolveNoticeImagesAsync(Notice notice)
    {
        noticeImageRequest.Cancel();noticeImageRequest.Dispose();noticeImageRequest=new();
        var publish=settings.MspPublishEvents;var generation=msp.Generation;
        var image=await noticeImages.ResolveAsync(notice,noticeImageRequest.Token);
        if(!closing && publish && settings.MspPublishEvents && generation==msp.Generation)msp.PublishNotice(notice,settings.PrivatePreview,image ?? (notifications.Logo(notice.SourceId) is {} logo ? new NoticeImages.Image(logo,false) : null));
        if(!closing && ReferenceEquals(state.Notification,notice))Render();
    }
    private void InitializeNotifications()
    {
        Closed+=(_,_)=>{noticeImageRequest.Cancel();noticeImageRequest.Dispose();noticeImages.Dispose();};
        Shell.PreviewMouseLeftButtonDown+=(_,args)=>
        {
            if(state.Notification==null)return;
            noticePress=args.GetPosition(this);pressedNotice=state.Notification;noticeSwiped=false;
            if(settings.SwipeNotifications)Mouse.Capture(Shell,CaptureMode.SubTree);
        };
        Shell.PreviewMouseMove+=(_,args)=>
        {
            if(!settings.SwipeNotifications || noticePress is not {} origin || args.LeftButton!=MouseButtonState.Pressed || !ReferenceEquals(pressedNotice,state.Notification))return;
            if(TrySwipeNotification(pressedNotice,origin.Y-args.GetPosition(this).Y)){noticeSwiped=true;args.Handled=true;}
        };
        Shell.PreviewMouseLeftButtonUp+=(_,args)=>
        {
            if(noticePress==null)return;
            var suppress=noticeSwiped || !ReferenceEquals(pressedNotice,state.Notification);
            if(Shell.IsMouseCaptured)Shell.ReleaseMouseCapture();
            if(suppress){args.Handled=true;noticeSwiped=false;}
            noticePress=null;pressedNotice=null;
        };
        Shell.LostMouseCapture+=(_,_)=>{noticePress=null;pressedNotice=null;};
        NoticeIconFrame.MouseLeftButtonUp+=(_,args)=>
        {
            if(state.Notification is not {} notice || state.Expanded)return;
            args.Handled=true;
            if(!AppActivation.Open(notice.SourceId))Design.PresentMessage("无法唤回应用","未找到对应应用窗口，请从任务栏或开始菜单打开。");
            else Dismiss();
        };
    }
    internal bool TrySwipeNotification(Notice? expected,double upwardDistance)
    {
        if(!settings.SwipeNotifications || expected==null || !ReferenceEquals(expected,state.Notification) || upwardDistance<12)return false;
        Dismiss();return true;
    }
    private double NotificationWidth(double cap)
    {
        if(state.Notification is not {} notice)return cap;
        double Measure(string text,double size)=>new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        if(settings.PrivatePreview && !state.Expanded)return Math.Min(cap,Math.Max(140,Measure(notice.App,13)+28));
        var title=(state.GroupCount>1 ? $"[{state.GroupCount}条] " : "")+notice.Title;
        var textWidth=Math.Max(Measure(title,13),Measure(notice.Body,11));
        if(settings.NotificationMode==2)textWidth=Math.Max(textWidth,Measure(notice.App,11));
        var side=settings.NotificationMode==1 ? Math.Min(64,Math.Max(Measure(notice.App,10),Measure("现在",10)))+7 : settings.NotificationMode==2 ? 36 : 0;
        var imageSize=settings.NotificationIconSize+(noticeImages.Get(notice)?.Avatar==true ? 5 : 0);
        return state.Expanded ? cap : Math.Min(cap,Math.Max(104,textWidth+Math.Min(imageSize,Math.Max(1,cap-64))+settings.NotificationIconGap+30+side));
    }
    private double RenderNotification(double width)
    {
        if(state.Notification is not {} notice)
        {
            NoticeApp.Text=NoticeTitle.Text=NoticeBody.Text=NoticePrivateApp.Text=NoticeRightApp.Text="";
            NoticeIcon.Source=defaultNoticeIcon;return settings.NotificationHeight;
        }
        var preview=settings.PrivatePreview && !state.Expanded;
        var full=settings.NotificationMode==2;
        var compact=settings.NotificationMode==1;
        var app=notice.App;
        NoticePrivateApp.Text=app;NoticePrivate.Visibility=preview ? Visibility.Visible : Visibility.Collapsed;
        NoticeContent.Visibility=preview ? Visibility.Collapsed : Visibility.Visible;
        NoticeIconFrame.Visibility=preview || state.Expanded ? Visibility.Collapsed : Visibility.Visible;
        NoticeRight.Visibility=!preview && !state.Expanded && (compact || full) ? Visibility.Visible : Visibility.Collapsed;
        NoticeRight.VerticalAlignment=full ? VerticalAlignment.Top : VerticalAlignment.Center;
        NoticeRightApp.Visibility=compact ? Visibility.Visible : Visibility.Collapsed;
        NoticeRightApp.Text=app;NoticeApp.Text=app;
        NoticeApp.Visibility=full || state.Expanded ? Visibility.Visible : Visibility.Collapsed;
        NoticeTitle.Visibility=state.Expanded ? Visibility.Collapsed : Visibility.Visible;
        NoticeTitle.Text=(state.GroupCount>1 ? $"[{state.GroupCount}条] " : "")+notice.Title;
        NoticeBody.Text=state.Expanded ? notice.Title+"\n"+notice.Body : notice.Body.Replace('\r',' ').Replace('\n',' ');
        NoticeBody.FontSize=state.Expanded ? 12 : 11;
        NoticeBody.TextWrapping=state.Expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
        NoticeBody.LineHeight=state.Expanded ? 18 : double.NaN;
        NoticeBodyScroll.VerticalScrollBarVisibility=state.Expanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        NoticeBodyScroll.Margin=new Thickness(0,state.Expanded ? 6 : 3,0,0);
        var supplied=noticeImages.Get(notice);
        NoticeIcon.Source=supplied?.Bitmap ?? notifications.Logo(notice.SourceId) ?? defaultNoticeIcon;
        NoticeIcon.Stretch=Stretch.UniformToFill;
        var size=Math.Min(settings.NotificationIconSize+(supplied?.Avatar==true ? 5 : 0),Math.Max(1,width-64));
        NoticeIconFrame.Width=NoticeIconFrame.Height=size;
        NoticeIcon.Clip=new RectangleGeometry(new Rect(0,0,size,size),supplied?.Avatar==true ? size/2 : 4,supplied?.Avatar==true ? size/2 : 4);
        NoticeIconFrame.Margin=new Thickness(0,0,settings.NotificationIconGap,0);
        if(state.Expanded)
        {
            var text=new FormattedText(NoticeBody.Text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),12,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(1,width-30),LineHeight=18};
            return Math.Clamp(36+Math.Ceiling(text.Height),54,162);
        }
        return Math.Max(settings.NotificationHeight,preview ? 55 : Math.Max(size+8,full ? 66 : 48));
    }
}
