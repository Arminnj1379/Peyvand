using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Files;
using Messenger.Domain.Messages;

namespace Messenger.Domain.Tests;

public sealed class FileTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Mp4 = [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];
    private static readonly byte[] Mp3 = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00];

    [Theory]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("notes.txt", "text/plain")]
    public void Validate_accepts_supported_types(string name, string contentType)
    {
        var bytes = contentType.StartsWith("image/png") ? Png
            : contentType.StartsWith("image/") ? Jpeg
            : contentType.StartsWith("video/") ? Mp4
            : contentType.StartsWith("audio/") ? Mp3
            : contentType.StartsWith("application/pdf") ? Pdf
            : "hello"u8.ToArray();

        var result = FileValidation.Validate(bytes, name, contentType);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_rejects_unknown_extension()
    {
        var result = FileValidation.Validate(Png, "evil.exe", "application/x-msdownload");

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.UnsupportedType, result.Error);
    }

    [Fact]
    public void Validate_rejects_extension_type_mismatch()
    {
        // PNG bytes wearing a JPEG name.
        var result = FileValidation.Validate(Png, "photo.jpg", "image/jpeg");

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ContentMismatch, result.Error);
    }

    [Fact]
    public void Validate_rejects_extension_contenttype_mismatch()
    {
        var result = FileValidation.Validate(Png, "photo.png", "image/jpeg");

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ContentMismatch, result.Error);
    }

    [Fact]
    public void Validate_rejects_empty_bytes()
    {
        var result = FileValidation.Validate([], "photo.png", "image/png");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_rejects_oversize()
    {
        var big = new byte[FileValidation.MaxImageBytes + 1];
        Png.CopyTo(big, 0);

        var result = FileValidation.Validate(big, "photo.png", "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.TooLarge, result.Error);
    }

    [Fact]
    public void Validate_normalizes_content_type_aliases()
    {
        Assert.Equal("image/jpeg", FileValidation.NormalizeContentType("image/jpg; charset=binary"));
        Assert.Equal("audio/wav", FileValidation.NormalizeContentType("audio/x-wav"));
    }

    [Theory]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("my photo(1).png", "my_photo_1_.png")]
    [InlineData("???", "file")]
    public void SanitizeFileName_neutralizes_paths_and_symbols(string input, string expected)
    {
        Assert.Equal(expected, FileValidation.SanitizeFileName(input));
    }

    [Fact]
    public void BuildObjectKey_is_server_generated_and_unguessable()
    {
        var key = FileValidation.BuildObjectKey("abc123", "photo.png");

        Assert.Equal("files/abc123/photo.png", key);
        Assert.DoesNotContain("..", key);
    }

    [Fact]
    public void Create_stores_canonical_metadata()
    {
        var result = ChatFile.Create("f1", "u1", Png, "My Photo.PNG", "image/png", Now);

        Assert.True(result.IsSuccess);
        var file = result.Value!;
        Assert.Equal("My_Photo.PNG", file.FileName);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(FileKind.Image, file.Kind);
        Assert.Equal(Png.Length, file.SizeBytes);
        Assert.Equal(64, file.Sha256.Length);
        Assert.Equal(FileStatus.Ready, file.Status);
        Assert.False(file.IsAttached);
        Assert.StartsWith("files/f1/", file.ObjectKey);
    }

    [Fact]
    public void Attach_links_once()
    {
        var file = ChatFile.Create("f1", "u1", Png, "a.png", "image/png", Now).Value!;

        Assert.True(file.Attach("c1", "m1", Now).IsSuccess);
        Assert.True(file.IsAttached);
        Assert.Equal("c1", file.ChatId);
        Assert.Equal("m1", file.MessageId);

        var again = file.Attach("c1", "m2", Now);
        Assert.True(again.IsFailure);
        Assert.Equal(FileErrors.AlreadyAttached, again.Error);
    }

    [Fact]
    public void Message_create_allows_captionless_attachments()
    {
        var withFile = Message.Create("m1", "c1", 1, "k-1", "u1",
            MessageKind.Text, "   ", null, null, Now, "f1");

        Assert.True(withFile.IsSuccess);
        Assert.Equal("f1", withFile.Value!.FileId);
        Assert.True(withFile.Value.HasAttachment);

        var withoutFile = Message.Create("m2", "c1", 2, "k-2", "u1",
            MessageKind.Text, "   ", null, null, Now);

        Assert.True(withoutFile.IsFailure);
        Assert.Equal(MessageErrors.InvalidBody, withoutFile.Error);
    }

    [Fact]
    public void Message_edit_allows_clearing_captions_on_attachments()
    {
        var message = Message.Create("m1", "c1", 1, "k-1", "u1",
            MessageKind.Text, "caption", null, null, Now, "f1").Value!;

        Assert.True(message.Edit("", Now).IsSuccess);
        Assert.Equal(string.Empty, message.Body);

        var text = Message.Create("m2", "c1", 2, "k-2", "u1",
            MessageKind.Text, "hi", null, null, Now).Value!;
        var cleared = text.Edit("  ", Now);
        Assert.True(cleared.IsFailure);
        Assert.Equal(MessageErrors.InvalidBody, cleared.Error);
    }

    [Fact]
    public void Rehydrate_round_trips_file_id()
    {
        var message = Message.Create("m1", "c1", 1, "k-1", "u1",
            MessageKind.Text, "", null, null, Now, "f9").Value!;

        var revived = Message.Rehydrate(new MessageSnapshot(
            message.Id, message.ChatId, message.Seq, message.ClientMessageId,
            message.SenderId, message.Kind, message.Body, message.ReplyToMessageId,
            message.ForwardFrom, message.IsEdited, message.EditedAt,
            message.IsDeleted, message.DeletedAt, message.DeletedBy, message.CreatedAt,
            [], 0, message.FileId));

        Assert.Equal("f9", revived.FileId);
        Assert.Empty(revived.DomainEvents);
    }

    [Fact]
    public void PreviewLabel_per_kind()
    {
        Assert.Equal("📷 Photo", FileValidation.PreviewLabel(FileKind.Image, "a.png"));
        Assert.Equal("🎞️ GIF", FileValidation.PreviewLabel(FileKind.Image, "fun.gif"));
        Assert.Equal("🎬 Video", FileValidation.PreviewLabel(FileKind.Video, "a.mp4"));
        Assert.Equal("🎵 Audio", FileValidation.PreviewLabel(FileKind.Audio, "a.mp3"));
        Assert.Equal("🎤 Voice", FileValidation.PreviewLabel(FileKind.Audio, "voice-abc.webm"));
        Assert.Equal("📎 report.pdf", FileValidation.PreviewLabel(FileKind.Document, "report.pdf"));
    }

    [Fact]
    public void Validate_accepts_voice_recorder_webm_with_weba_bytes()
    {
        // Chrome/Edge/Firefox MediaRecorder default: audio/webm bytes in a .webm file.
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x93, 0x42, 0x82, 0x88 };

        var result = FileValidation.Validate(webm, "voice-abc123.webm", "audio/webm;codecs=opus");

        Assert.True(result.IsSuccess);
        Assert.Equal(FileKind.Audio, result.Value);
    }

    [Fact]
    public void Validate_accepts_safari_voice_recorder_mp4_audio()
    {
        // Safari MediaRecorder: audio/mp4 bytes in a .mp4 file.
        var result = FileValidation.Validate(Mp4, "voice-abc123.mp4", "audio/mp4");

        Assert.True(result.IsSuccess);
        Assert.Equal(FileKind.Audio, result.Value);
    }

    [Theory]
    [InlineData("note.opus", "audio/opus")]
    [InlineData("note.aac", "audio/aac")]
    [InlineData("note.flac", "audio/flac")]
    public void Validate_accepts_modern_audio_codecs(string name, string contentType)
    {
        var bytes = contentType switch
        {
            "audio/opus" => "OggS"u8.ToArray(),
            "audio/aac" => "ID3"u8.ToArray(),
            _ => "fLaC"u8.ToArray(),
        };

        var result = FileValidation.Validate(bytes, name, contentType);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileKind.Audio, result.Value);
    }

    [Fact]
    public void Validate_accepts_gif_as_image()
    {
        var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };

        var result = FileValidation.Validate(gif, "fun.gif", "image/gif");

        Assert.True(result.IsSuccess);
        Assert.Equal(FileKind.Image, result.Value);
    }
}
