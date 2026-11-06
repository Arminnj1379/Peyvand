namespace Messenger.Domain.Files;

/// <summary>Lifecycle of a stored file. Uploads land Ready synchronously in MVP
/// (scan runs inline); the state exists so a future async pipeline
/// (quarantine → scan → release) needs no schema change.</summary>
public enum FileStatus
{
    Pending = 1,
    Ready = 2,
    Rejected = 3
}
