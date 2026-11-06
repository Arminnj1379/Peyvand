using Messenger.Domain.Common;

namespace Messenger.Domain.Files;

/// <summary>Machine-readable file/media failures. Messages are safe to surface.</summary>
public static class FileErrors
{
    public static readonly Error NotFound =
        new("file.not_found", "File not found.");

    public static readonly Error TooLarge =
        new("file.too_large", "File is larger than the allowed limit for its type.");

    public static readonly Error UnsupportedType =
        new("file.unsupported_type", "This file type is not supported.");

    public static readonly Error ContentMismatch =
        new("file.content_mismatch", "File contents do not match the declared file type.");

    public static readonly Error InvalidName =
        new("file.invalid_name", "File name is not usable.");

    public static readonly Error NotReady =
        new("file.not_ready", "File is not ready yet.");

    public static readonly Error AlreadyAttached =
        new("file.already_attached", "File is already attached to a message.");

    public static readonly Error NotOwner =
        new("file.not_owner", "Only the uploader can use this file.");

    public static readonly Error ScanRejected =
        new("file.scan_rejected", "File was rejected by the malware scan.");

    public static readonly Error StorageUnavailable =
        new("file.storage_unavailable", "File storage is temporarily unavailable.");
}
