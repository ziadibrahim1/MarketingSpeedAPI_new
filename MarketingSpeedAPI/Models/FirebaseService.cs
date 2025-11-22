using FirebaseAdmin.Messaging;

public static class FirebaseService
{
    public static async Task SendPushAsync(
        string deviceToken,
        string title,
        string body)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
            return;

        var message = new Message()
        {
            Token = deviceToken,
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High
            }
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            Console.WriteLine($"🔥 Push sent: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Push failed: {ex.Message}");
        }
    }
}
