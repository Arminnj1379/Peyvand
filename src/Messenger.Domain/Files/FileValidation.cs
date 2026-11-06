using System.Text;
using Messenger.Domain.Common;

namespace Messenger.Domain.Files;

/// <summary>
/// Upload validation: size limits per kind, extension ↔ content-type consistency,
/// and magic-number sniffing so the declared type must match the actual bytes.
/// Pure static rules — no I/O, no infrastructure.
/// </summary>
public static class FileValidation
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxVideoBytes = 100 * 1024 * 1024;
    public const int MaxAudioBytes = 20 * 1024 * 1024;
    public const int MaxDocumentBytes = 25 * 1024 * 1024;
    public const int MaxFileNameLength = 128;

    private static readonly Dictionary<string, (string ContentType, FileKind Kind)> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ("image/jpeg", FileKind.Image),
            [".jpeg"] = ("image/jpeg", FileKind.Image),
            [".png"] = ("image/png", FileKind.Image),
            [".webp"] = ("image/webp", FileKind.Image),
            [".gif"] = ("image/gif", FileKind.Image),
            [".mp4"] = ("video/mp4", FileKind.Video),
            [".webm"] = ("video/webm", FileKind.Video),
            [".mov"] = ("video/quicktime", FileKind.Video),
            [".mp3"] = ("audio/mpeg", FileKind.Audio),
            [".wav"] = ("audio/wav", FileKind.Audio),
            [".ogg"] = ("audio/ogg", FileKind.Audio),
            [".oga"] = ("audio/ogg", FileKind.Audio),
            [".weba"] = ("audio/webm", FileKind.Audio),
            [".m4a"] = ("audio/mp4", FileKind.Audio),
            [".opus"] = ("audio/opus", FileKind.Audio),
            [".aac"] = ("audio/aac", FileKind.Audio),
            [".flac"] = ("audio/flac", FileKind.Audio),
            [".pdf"] = ("application/pdf", FileKind.Document),
            [".txt"] = ("text/plain", FileKind.Document),
            [".csv"] = ("text/csv", FileKind.Document),
            [".md"] = ("text/markdown", FileKind.Document),
            [".zip"] = ("application/zip", FileKind.Document),
            [".doc"] = ("application/msword", FileKind.Document),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileKind.Document),
            [".xls"] = ("application/vnd.ms-excel", FileKind.Document),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileKind.Document),
            [".ppt"] = ("application/vnd.ms-powerpoint", FileKind.Document),
            [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", FileKind.Document),
        };

    private static readonly Dictionary<string, FileKind> ContentTypeMap =
        ExtensionMap.Values.Distinct().ToDictionary(v => v.ContentType, v => v.Kind);

    // Browsers send a few aliases; normalize them before matching.
    // MediaRecorder voices arrive with codec suffixes (e.g. audio/webm;codecs=opus
    // on Chrome, audio/mp4 on Safari) — the ';…' suffix is stripped in Normalize.
    private static readonly Dictionary<string, string> ContentTypeAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpg"] = "image/jpeg",
            ["audio/x-wav"] = "audio/wav",
            ["audio/wave"] = "audio/wav",
            ["audio/x-m4a"] = "audio/mp4",
            ["audio/x-opus"] = "audio/opus",
            ["audio/x-flac"] = "audio/flac",
            ["audio/x-aac"] = "audio/aac",
            ["video/x-m4v"] = "video/mp4",
            ["text/x-markdown"] = "text/markdown",
        };

    public static int MaxBytesFor(FileKind kind) => kind switch
    {
        FileKind.Image => MaxImageBytes,
        FileKind.Video => MaxVideoBytes,
        FileKind.Audio => MaxAudioBytes,
        _ => MaxDocumentBytes
    };

    /// <summary>Validates bytes + declared name/type. Returns the file kind on success.</summary>
    public static Result<FileKind> Validate(byte[] bytes, string fileName, string contentType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            return Result<FileKind>.Fail(FileErrors.UnsupportedType);
        }

        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentType))
        {
            return Result<FileKind>.Fail(FileErrors.InvalidName);
        }

        var normalizedType = NormalizeContentType(contentType);
        if (!ContentTypeMap.TryGetValue(normalizedType, out var kind))
        {
            return Result<FileKind>.Fail(FileErrors.UnsupportedType);
        }

        if (bytes.Length > MaxBytesFor(kind))
        {
            return Result<FileKind>.Fail(FileErrors.TooLarge);
        }

        var extension = Path.GetExtension(fileName.Trim());
        if (string.IsNullOrEmpty(extension))
        {
            return Result<FileKind>.Fail(FileErrors.UnsupportedType);
        }

        if (!ExtensionAllows(extension, normalizedType, kind))
        {
            // Unknown extension, or the extension belongs to a different type.
            var mismatch = ExtensionMap.ContainsKey(extension);
            return Result<FileKind>.Fail(mismatch ? FileErrors.ContentMismatch : FileErrors.UnsupportedType);
        }

        if (!MatchesMagic(bytes, normalizedType))
        {
            return Result<FileKind>.Fail(FileErrors.ContentMismatch);
        }

        return Result<FileKind>.Ok(kind);
    }

    public static string NormalizeContentType(string contentType)
    {
        var mime = contentType.Trim().ToLowerInvariant().Split(';')[0].Trim();
        return ContentTypeAliases.TryGetValue(mime, out var canonical) ? canonical : mime;
    }

    /// <summary>
    /// Whether a filename extension may carry the given content type.
    /// Besides the 1:1 <see cref="ExtensionMap"/> entries, voice-recorder
    /// outputs are accepted: MediaRecorder defaults to <c>.webm</c> filenames
    /// with <c>audio/webm</c> bytes (Chrome/Edge/Firefox) and <c>.mp4</c>
    /// filenames with <c>audio/mp4</c> bytes (Safari) — rejecting those would
    /// break in-app voice messages, so both spellings map to Audio.
    /// </summary>
    private static bool ExtensionAllows(string extension, string normalizedType, FileKind kind)
    {
        if (ExtensionMap.TryGetValue(extension, out var expected) &&
            string.Equals(expected.ContentType, normalizedType, StringComparison.OrdinalIgnoreCase) &&
            expected.Kind == kind)
        {
            return true;
        }

        return (extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedType, "audio/webm", StringComparison.OrdinalIgnoreCase) &&
            kind == FileKind.Audio)
            || (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedType, "audio/mp4", StringComparison.OrdinalIgnoreCase) &&
            kind == FileKind.Audio);
    }

    /// <summary>Server-side storage key. Unguessable (ObjectId), never derived from user input.</summary>
    public static string BuildObjectKey(string fileId, string safeFileName) =>
        $"files/{fileId}/{safeFileName}";

    /// <summary>Strips directories, control chars, and unsafe symbols. Never empty.</summary>
    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        var sanitized = builder.ToString().Trim('.', '_');
        while (sanitized.Contains("__", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("__", "_", StringComparison.Ordinal);
        }

        if (sanitized.Length == 0)
        {
            return "file";
        }

        return sanitized.Length > MaxFileNameLength
            ? sanitized[^MaxFileNameLength..].TrimStart('.', '_')
            : sanitized;
    }

    public static string PreviewLabel(FileKind kind, string fileName) => kind switch
    {
        FileKind.Image => IsGif(fileName) ? "🎞️ GIF" : "📷 Photo",
        FileKind.Video => "🎬 Video",
        FileKind.Audio => IsVoiceNote(fileName) ? "🎤 Voice" : "🎵 Audio",
        _ => $"📎 {fileName}"
    };

    /// <summary>Voice notes are recorder outputs (frontend names them voice-*).</summary>
    public static bool IsVoiceNote(string fileName) =>
        Path.GetFileName(fileName).StartsWith("voice-", StringComparison.OrdinalIgnoreCase);

    /// <summary>GIFs animate natively in the client; they get their own preview label.</summary>
    public static bool IsGif(string fileName) =>
        Path.GetExtension(fileName).Equals(".gif", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesMagic(byte[] bytes, string contentType)
    {
        // Text formats have no reliable magic; the extension gate above applies.
        if (contentType.StartsWith("text/", StringComparison.Ordinal))
        {
            return true;
        }

        return contentType switch
        {
            "image/jpeg" => HasPrefix(bytes, [0xFF, 0xD8, 0xFF]),
            "image/png" => HasPrefix(bytes, [0x89, 0x50, 0x4E, 0x47]),
            "image/gif" => HasPrefix(bytes, "GIF8"u8),
            "image/webp" => bytes.Length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            "video/mp4" or "video/quicktime" => bytes.Length >= 12 &&
                bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70,
            "video/webm" => HasPrefix(bytes, [0x1A, 0x45, 0xDF, 0xA3]),
            "audio/mpeg" => HasPrefix(bytes, "ID3"u8) ||
                (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0),
            "audio/wav" => bytes.Length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x41 && bytes[10] == 0x56 && bytes[11] == 0x45,
            "audio/ogg" => HasPrefix(bytes, "OggS"u8),
            "audio/opus" => HasPrefix(bytes, "OggS"u8) || HasPrefix(bytes, "OpusHead"u8),
            "audio/aac" => HasPrefix(bytes, "ID3"u8) ||
                (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xF6) == 0xF0),
            "audio/flac" => HasPrefix(bytes, "fLaC"u8),
            "audio/webm" => HasPrefix(bytes, [0x1A, 0x45, 0xDF, 0xA3]),
            "audio/mp4" => bytes.Length >= 12 &&
                bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70,
            "application/pdf" => HasPrefix(bytes, "%PDF"u8),
            "application/zip" => HasPrefix(bytes, [0x50, 0x4B, 0x03, 0x04]),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => HasPrefix(bytes, [0x50, 0x4B, 0x03, 0x04]),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => HasPrefix(bytes, [0x50, 0x4B, 0x03, 0x04]),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => HasPrefix(bytes, [0x50, 0x4B, 0x03, 0x04]),
            "application/msword" => HasPrefix(bytes, [0xD0, 0xCF, 0x11, 0xE0]),
            "application/vnd.ms-excel" => HasPrefix(bytes, [0xD0, 0xCF, 0x11, 0xE0]),
            "application/vnd.ms-powerpoint" => HasPrefix(bytes, [0xD0, 0xCF, 0x11, 0xE0]),
            _ => false
        };
    }

    private static bool HasPrefix(byte[] bytes, ReadOnlySpan<byte> prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
