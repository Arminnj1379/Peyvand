namespace Messenger.Application.Services;

/// <summary>
/// Shared avatar byte sniffing (magic numbers, not the declared content type).
/// Replaces the identical copies that lived in the user and chat avatar handlers.
/// </summary>
internal static class AvatarValidation
{
    internal static bool HasValidMagicNumber(byte[] bytes, string contentType)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        return contentType switch
        {
            "image/jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8,
            "image/png" => bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            // RIFF....WEBP
            "image/webp" => bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            _ => false
        };
    }
}
