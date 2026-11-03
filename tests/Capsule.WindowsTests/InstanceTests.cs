using Capsule.App;
using System.Diagnostics;
using System.IO;

internal static class InstanceTests
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Contains("--instance-peer"))
        {
            var name = args[Array.IndexOf(args, "--instance-peer") + 1];
            using var secondary = new SingleInstance(name, () => throw new Exception("Secondary must not listen"));
            if (secondary.IsPrimary || !SingleInstance.Notify(name)) throw new Exception("Secondary did not notify primary");
            return;
        }
        var identity = "Local\\LingdongCapsule.Test." + Guid.NewGuid().ToString("N");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var primary = new SingleInstance(identity, () => notified.TrySetResult()))
        {
            if (!primary.IsPrimary) throw new Exception("Unique primary was rejected");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(InstanceTests).Assembly.Location);
            start.ArgumentList.Add("--instance-peer"); start.ArgumentList.Add(identity);
            using var child = Process.Start(start) ?? throw new Exception("Owned secondary could not start");
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (child.ExitCode != 0) throw new Exception("Owned secondary failed");
                await notified.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally { if (!child.HasExited) child.Kill(); }
        }
        if (SingleInstance.Notify(identity)) throw new Exception("Disposed activation event remained registered");
        using var replacement = new SingleInstance(identity, () => { });
        if (!replacement.IsPrimary) throw new Exception("Clean restart could not become primary");
        Console.WriteLine("PASS owned cross-process single instance, activation, release and restart");
        var app = new Capsule.App.App { SuppressStartupForTests = true };
        app.InitializeComponent(); app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        var settings = new Settings { MediaEnabled = false, NotificationsEnabled = false, OverviewEnabled = false };
        var window = new MainWindow(suppressOnboarding: true, initialSettings: settings) { ShowActivated = false };
        try
        {
            window.Show(); await Task.Delay(300); window.OpenSettings(); await Task.Delay(100);
            var page = app.Windows.OfType<SettingsWindow>().Single();
            page.WindowState = System.Windows.WindowState.Minimized;
            window.OpenSettings(); await Task.Delay(100);
            if (page.WindowState != System.Windows.WindowState.Normal || app.Windows.OfType<SettingsWindow>().Count() != 1)
                throw new Exception("Settings activation duplicated or left the window minimized");
            page.Close(); window.OpenSettings(); await Task.Delay(100);
            if (app.Windows.OfType<SettingsWindow>().Count() != 1) throw new Exception("Closed settings could not reopen");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS actual settings reopen and minimized window restore without duplicates; foreground policy not asserted");
    }
}
