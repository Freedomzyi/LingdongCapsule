using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Capsule.PluginApi;

namespace Capsule.App;

internal sealed class PluginModule : IDisposable
{
    private sealed class Context(string entry) : AssemblyLoadContext(isCollectible:true)
    {
        private readonly AssemblyDependencyResolver resolver=new(entry);
        protected override Assembly? Load(AssemblyName name)
        {
            if(name.Name==typeof(ICapsulePlugin).Assembly.GetName().Name)return typeof(ICapsulePlugin).Assembly;
            var path=resolver.ResolveAssemblyToPath(name);return path==null ? null : LoadFromAssemblyPath(path);
        }
        protected override IntPtr LoadUnmanagedDll(string name)
        {
            var path=resolver.ResolveUnmanagedDllToPath(name);return path==null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
    private Context? context;
    public ICapsulePlugin? Plugin{get;private set;}
    public PluginModule(string file)
    {
        var path=Path.GetFullPath(file);var load=new Context(path);
        try
        {
            var assembly=load.LoadFromAssemblyPath(path);
            var entries=assembly.GetExportedTypes().Where(type=>!type.IsAbstract && typeof(ICapsulePlugin).IsAssignableFrom(type)).ToArray();
            if(entries.Length!=1)throw new InvalidDataException("插件必须导出一个 ICapsulePlugin 实现。");
            var plugin=(ICapsulePlugin)Activator.CreateInstance(entries[0])!;Plugin=plugin;
            var identity=plugin.Identity;
            if(identity==null || !Regex.IsMatch(identity.Id ?? "",@"^[a-z][a-z0-9.-]{2,79}$") || string.IsNullOrWhiteSpace(identity.DisplayName) || identity.DisplayName.Length>80 || !Version.TryParse(identity.Version,out _))throw new InvalidDataException("插件身份或版本无效。");
            context=load;
        }
        catch{try{Plugin?.Dispose();}finally{Plugin=null;load.Unload();}throw;}
    }
    public void Dispose()
    {
        var active=Plugin;Plugin=null;var load=context;context=null;
        try{active?.Dispose();}finally{load?.Unload();}
    }
}
