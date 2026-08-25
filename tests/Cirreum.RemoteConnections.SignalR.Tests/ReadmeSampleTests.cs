namespace Cirreum.RemoteConnections.SignalR.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Compiles the connection type and the registration the README documents. A sample that no
/// longer matches the surface fails the build here rather than at a reader.
/// </summary>
public class ReadmeSampleTests {

	public sealed record ChatMessage(string Room, string Text);

	// README — "Derive a connection type"
	public sealed class ChatConnection(SignalRRemoteConnectionContext context)
		: SignalRRemoteConnection(context) {

		public IDisposable OnMessage(Func<ChatMessage, Task> handler) =>
			this.On("ReceiveMessage", handler);

		// A client method the hub invokes with several arguments
		public IDisposable OnToolComplete(Func<string, bool, Task> handler) =>
			this.On("ReceiveToolComplete", handler);

		// Fire-and-forget, one argument
		public Task SendMessageAsync(ChatMessage message, CancellationToken ct = default) =>
			this.SendAsync("SendMessage", message, ct);

		// Several arguments
		public Task SendToRoomAsync(string room, string text, CancellationToken ct = default) =>
			this.SendAsync("SendToRoom", [room, text], ct);

		// Request/response
		public Task<string> StartConversationAsync(string context, CancellationToken ct = default) =>
			this.InvokeAsync<string>("StartConversation", [context], ct);

	}

	[Fact]
	public void The_documented_registration_compiles_and_resolves() {

		var services = new ServiceCollection();

		// README — "Register it"
		services.AddSingleton(sp => new ChatConnection(
			SignalRRemoteConnectionContext.Create<ChatConnection>(sp, new RemoteConnectionOptions("MyApp") {
				EndpointUri = new Uri("https://api.example.com/hubs/chat"),
				Scopes = ["api://contoso/access_as_user"],
			})));

		services.AddSingleton<IRemoteConnection>(sp => sp.GetRequiredService<ChatConnection>());

		services.Should().Contain(d => d.ServiceType == typeof(IRemoteConnection));

	}

}
