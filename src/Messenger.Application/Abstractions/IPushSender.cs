namespace Messenger.Application.Abstractions;

/// <summary>
/// Push fan-out port. MVP implementation is Firebase; air-gapped on-prem
/// deployments resolve a no-op sender (in-app only). Message bodies are
/// never included — push carries a pull ping only.
/// </summary>
public interface IPushSender
{
    Task SendAsync(string userId, string title, string body, CancellationToken cancellationToken);
}
