using Capsule.Core;

internal static class GlassMaterialTests
{
    internal static void Run()
    {
        const int width=160,height=60,margin=24,sw=width+margin*2,sh=height+margin*2;
        var source=new byte[sw*sh*4];
        for(var y=0;y<sh;y++)for(var x=0;x<sw;x++){var i=(y*sw+x)*4;source[i]=(byte)(x*13%256);source[i+1]=(byte)(y*19%256);source[i+2]=(byte)((x/9+y/7)%2*200+20);source[i+3]=255;}
        var original=(byte[])source.Clone();
        byte[] Render(GlassOptions o,bool acrylic=false,bool dark=false,double opacity=1)=>GlassMaterial.Compose(source,sw,sh,width,height,margin,30,30,o,acrylic,dark,opacity);
        var options=new GlassOptions();var baseline=Render(options);
        foreach(var changed in new[]{options with{Blur=0},options with{Refraction=0},options with{Dispersion=0},options with{Rim=0},options with{Thickness=0},options with{Depth=0},options with{Saturation=0},options with{Frost=0},options with{Brightness=0},options with{Zoom=4}})
            if(Render(changed).SequenceEqual(baseline))throw new Exception("Glass parameter has no effect on asymmetric backdrop");
        if(!source.SequenceEqual(original) || baseline.Length!=width*height*4 || baseline.Where((_,i)=>i%4==3).Any(v=>v!=255))throw new Exception("Glass source or output contract violated");
        var transparent=Render(options,opacity:0);
        if(transparent.Where((_,i)=>i%4==3).Any(v=>v!=0) || Render(options,true).SequenceEqual(baseline) || Render(options,dark:true).SequenceEqual(baseline))throw new Exception("Material mode, theme or opacity failed");
        for(var level=0;level<5;level++)
        {
            var o=options with{Capture=level,FrameRate=level};
            if(o.CaptureMilliseconds!=new[]{1000,500,250,160,60}[level] || o.FramesPerSecond!=new[]{30,60,90,120,144}[level])throw new Exception("Material timing level mismatch");
        }
        var constant=Enumerable.Repeat((byte)120,source.Length).ToArray();for(var i=3;i<constant.Length;i+=4)constant[i]=255;
        var neutral=new GlassOptions{Blur=4,Refraction=0,Dispersion=0,Rim=0,Depth=0,Saturation=2,Frost=0,Brightness=2};
        var flat=GlassMaterial.Compose(constant,sw,sh,width,height,margin,30,30,neutral,false,false);
        if(flat.Where((_,i)=>i%4!=3).Any(v=>v!=120))throw new Exception("Blur does not preserve constant energy or has uninitialized weights");
        void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid material input accepted");}
        Reject(()=>GlassMaterial.Compose(source,sw,sh,-1,height,margin,30,30,options,false,false));
        Reject(()=>GlassMaterial.Compose([],sw,sh,width,height,margin,30,30,options,false,false));
        Reject(()=>GlassMaterial.Compose(source,sw,sh,width,height,margin,double.NaN,30,options,false,false));
        Reject(()=>GlassMaterial.Compose(source,sw,sh,width,height,margin,30,30,options,false,false,pixelsPerDip:double.PositiveInfinity));
        Console.WriteLine("PASS Glass material parameters, timing, bounded geometry, unchanged source, theme/alpha and blur energy");
    }
}
