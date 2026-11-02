using Capsule.App;
using Windows.UI.Notifications.Management;

// Read-only OS integration probe. Never requests permissions or outputs message contents.
var media = new MediaService();
var snapshot = await media.ReadAsync();
Console.WriteLine($"Media: {media.Status}; available={snapshot.Available}; playing={snapshot.Playing}");
try
{
    var listener = UserNotificationListener.Current;
    Console.WriteLine($"Notification access: {listener.GetAccessStatus()}");
    if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
    {
        var items = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
        Console.WriteLine($"Notification read succeeded; count={items.Count}");
    }
}
catch (Exception ex) { Console.WriteLine($"Notification unavailable: {ex.GetType().Name}; HRESULT=0x{ex.HResult:X8}"); }
