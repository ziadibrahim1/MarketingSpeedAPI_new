using Microsoft.AspNetCore.SignalR;

namespace MarketingSpeedAPI.Hubs
{
    public class ChatHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"SignalR: Connected {Context.ConnectionId}");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"SignalR: Disconnected {Context.ConnectionId}");
            return base.OnDisconnectedAsync(exception);
        }

        // client calls this to join its session group
        public async Task JoinSession(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");
        }

        // optional: client can send message via hub to session
        public Task SendMessageToSession(string sessionId, string text)
        {
            return Clients.Group(sessionId).SendAsync("ReceiveMessage", "server", text, DateTime.Now.ToString("o"));
        }
    }
}
