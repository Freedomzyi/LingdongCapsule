using Microsoft.Win32;
namespace Capsule.App;

internal static class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunPath); return key?.GetValue("LingdongCapsule") is string value && !string.IsNullOrWhiteSpace(value); }
    }
    public static void SetEnabled(bool enabled)
        =>SetEnabledTransactional(enabled,()=>{});
    internal static void SetEnabledTransactional(bool enabled,Action persist,string valueName="LingdongCapsule",string? executable=null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        var existed=key.GetValueNames().Contains(valueName,StringComparer.OrdinalIgnoreCase);
        var previous=key.GetValue(valueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind=existed ? key.GetValueKind(valueName) : RegistryValueKind.String;
        try
        {
            if(enabled)key.SetValue(valueName,'"'+(executable ?? Environment.ProcessPath)+'"'+" --startup",RegistryValueKind.String);
            else key.DeleteValue(valueName,false);
            persist();
        }
        catch(Exception original)
        {
            try{if(existed)key.SetValue(valueName,previous!,kind);else key.DeleteValue(valueName,false);}
            catch(Exception recovery){throw new AggregateException("自启设置失败，且原登记未能恢复。",original,recovery);}
            throw;
        }
    }
}
