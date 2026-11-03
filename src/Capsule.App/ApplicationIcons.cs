using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Capsule.App;

internal static class ApplicationIcons
{
    // Shell extraction can invoke providers; keep it off the dispatcher and bound concurrent work.
    private static readonly SemaphoreSlim workers=new(2);
    [StructLayout(LayoutKind.Sequential)]private struct NativeSize{public int Width,Height;}
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ImageFactory{[PreserveSig]int GetImage(NativeSize size,uint flags,out IntPtr bitmap);}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]private static extern int SHCreateItemFromParsingName(string path,IntPtr bind,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out ImageFactory? factory);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    internal static async Task<BitmapSource?> ReadAsync(string source,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(source) || source.Length>512 || source.Contains('\0'))return null;
        string name;
        if(Path.IsPathFullyQualified(source))
        {
            if(source.StartsWith(@"\\",StringComparison.Ordinal) || !source.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return null;
            name=source;
        }
        else
        {
            if(source.IndexOfAny(['\\','/',':'])>=0)return null;
            name=@"shell:AppsFolder\"+source;
        }
        return await Task.Run(() =>
        {
            workers.Wait(token);
            try
            {
                token.ThrowIfCancellationRequested();var iid=typeof(ImageFactory).GUID;ImageFactory? factory=null;IntPtr bitmap=IntPtr.Zero;
                try
                {
                    if(SHCreateItemFromParsingName(name,IntPtr.Zero,ref iid,out factory)<0 || factory==null)return null;
                    if(factory.GetImage(new NativeSize{Width=96,Height=96},4,out bitmap)<0 || bitmap==IntPtr.Zero || token.IsCancellationRequested)return null;
                    var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;
                }
                finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null && Marshal.IsComObject(factory))Marshal.ReleaseComObject(factory);}
            }
            catch(Exception){return null;}
            finally{workers.Release();}
        },token);
    }
}
