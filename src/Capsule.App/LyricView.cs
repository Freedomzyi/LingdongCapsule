using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Capsule.Core;

namespace Capsule.App;

public sealed class LyricView : FrameworkElement
{
    private sealed record TextLayout(FormattedText Dim, FormattedText Bright);
    private readonly Dictionary<(string,double),TextLayout> layouts=[];
    private readonly Dictionary<LyricLine,double[]> wordWidths=[];
    private double cachedDpi;
    private Typeface typeface=new("Microsoft YaHei UI");
    private bool dark=true;
    public void SetDark(bool value){if(dark==value)return;dark=value;layouts.Clear();InvalidateVisual();}
    public void SetFont(FontFamily family){if(typeface.FontFamily.Source==family.Source)return;typeface=new(family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);layouts.Clear();wordWidths.Clear();InvalidateVisual();}
    private bool listening;
    private bool playing;
    private LyricFrame previous, last;
    private long changedAt;
    private RectangleGeometry? bounds;
    public Func<LyricFrame>? FrameProvider {get;set;}
    public bool TranslationEnabled {get;set;}=true;
    public bool KaraokeEnabled {get;set;}=true;
    public bool ScrollEnabled {get;set;}=true;
    public bool Playing {get=>playing;set{playing=value;UpdateSubscription();}}

    public LyricView()
    {
        ClipToBounds=true;
        IsVisibleChanged+=(_,_)=>UpdateSubscription();
        Loaded+=(_,_)=>UpdateSubscription();
        Unloaded+=(_,_)=>{CompositionTarget.Rendering-=OnFrame;listening=false;};
        SizeChanged+=(_,_)=>{bounds=new(new Rect(RenderSize));InvalidateVisual();};
    }
    public void Refresh(){UpdateSubscription();InvalidateVisual();}
    private void UpdateSubscription()
    {
        var needed=(playing || previous.Line is not null) && IsVisible && IsLoaded;
        if(needed==listening)return;
        if(needed)CompositionTarget.Rendering+=OnFrame;else CompositionTarget.Rendering-=OnFrame;
        listening=needed;
    }
    private readonly AnimationCadence cadence=new();
    public int FrameRate{get=>cadence.FramesPerSecond;set=>cadence.FramesPerSecond=value;}
    private void OnFrame(object? sender,EventArgs args)
    {
        var finished=previous.Line is not null && Stopwatch.GetElapsedTime(changedAt).TotalMilliseconds>=180;
        if(finished){previous=default;UpdateSubscription();}
        if(finished || cadence.Advance(args))InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if(ActualWidth<1 || ActualHeight<1 || FrameProvider is null)return;
        var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(dpi!=cachedDpi){layouts.Clear();wordWidths.Clear();cachedDpi=dpi;}
        var frame=FrameProvider();
        if(!ReferenceEquals(frame.Line,last.Line)){previous=last;changedAt=Stopwatch.GetTimestamp();}
        last=frame;
        var fade=Math.Clamp(Stopwatch.GetElapsedTime(changedAt).TotalMilliseconds/180,0,1);
        context.PushClip(bounds ?? new RectangleGeometry(new Rect(RenderSize)));
        if(previous.Line is not null && fade<1){context.PushOpacity(1-fade);DrawFrame(context,previous);context.Pop();}
        context.PushOpacity(previous.Line is null ? 1 : fade);DrawFrame(context,frame);context.Pop();
        context.Pop();
        if(fade>=1)previous=default;
        UpdateSubscription();
    }
    private void DrawFrame(DrawingContext context,LyricFrame frame)
    {
        if(frame.Line is not {} row || row.Text.Length==0)return;
        var two=TranslationEnabled && row.Translation.Length>0;
        DrawLine(context,row.Text,12.5,ActualHeight/2+(two ? -2.5 : 4.5),frame,false);
        if(two)DrawLine(context,row.Translation,11.5,ActualHeight/2+12.5,frame,true);
    }
    private TextLayout Layout(string text,double size)
    {
        var key=(text,size);
        if(layouts.TryGetValue(key,out var found))return found;
        if(layouts.Count>40)layouts.Clear();
        var dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Make(Brush ink)=>new(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,typeface,size,ink,dpi);
        var result=new TextLayout(Make(new SolidColorBrush(Color.FromRgb(135,135,145))),Make(dark ? Brushes.White : Brushes.Black));
        layouts[key]=result;return result;
    }
    private void DrawLine(DrawingContext context,string text,double size,double baseline,LyricFrame frame,bool translated)
    {
        var layout=Layout(text,size);
        var width=layout.Bright.WidthIncludingTrailingWhitespace;
        var left=width<=ActualWidth ? (ActualWidth-width)/2 : ScrollEnabled ? -(width-ActualWidth)*frame.Progress : 0;
        var origin=new Point(left,baseline-layout.Bright.Baseline);
        if(!KaraokeEnabled){context.DrawText(layout.Bright,origin);return;}
        context.DrawText(layout.Dim,origin);
        var filled=width*frame.Progress;
        if(!translated && frame.Line!.Words.Count>0)
        {
            var row=frame.Line;
            if(!wordWidths.TryGetValue(row,out var edges))
            {
                if(wordWidths.Count>8)wordWidths.Clear();
                edges=new double[row.Words.Count+1];
                var textBuilder=new System.Text.StringBuilder();
                for(var i=0;i<row.Words.Count;i++)
                {
                    textBuilder.Append(row.Words[i].Text);
                    edges[i+1]=new FormattedText(textBuilder.ToString(),CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,typeface,size,Brushes.White,cachedDpi).WidthIncludingTrailingWhitespace;
                }
                wordWidths[row]=edges;
            }
            filled=0;
            for(var i=0;i<row.Words.Count;i++)
            {
                var word=row.Words[i];var before=edges[i];var after=edges[i+1];
                if(frame.PositionMs>=word.StartMs+word.DurationMs){filled=after;continue;}
                filled=before+(after-before)*Math.Clamp((frame.PositionMs-word.StartMs)/word.DurationMs,0,1);break;
            }
        }
        context.PushClip(new RectangleGeometry(new Rect(left,0,Math.Max(0,filled),ActualHeight)));
        context.DrawText(layout.Bright,origin);context.Pop();
    }
}
