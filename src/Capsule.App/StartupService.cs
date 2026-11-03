using Microsoft.Win32;
namespace Capsule.App;
internal static class StartupService
{
    private const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled
    {
        get { using var key=Registry.CurrentUser.OpenSubKey(RunPath);return key?.GetValue("LingdongCapsule") is string value && !string.IsNullOrWhiteSpace(value); }
    }
    public static void SetEnabled(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunPath);
        if(enabled)key.SetValue("LingdongCapsule",'"'+Environment.ProcessPath+'"'+" --startup");
        else key.DeleteValue("LingdongCapsule",false);
    }
}
