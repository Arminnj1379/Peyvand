using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Files;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Files;
using Messenger.Domain.Messages;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

/// <summary>Media slice: upload validation/scan/storage, send-with-file rules,
/// forward-shared bytes, download authorization, and delete semantics.</summary>
public sealed class MediaHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];
    private static readonly byte[] Mp4 =
        [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D, 0x01];

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeChatStore Chats = new();
        public FakeMessageStore Messages = new();
        public FakeFileStore Files = new();
        public FakeFileStorage Storage = new();
        public FakeFileScanner Scanner = new();
        public FakeClock Clock = new(Now);
        public FakeIds Ids = new();

        public async Task<User> SeedUserAsync(string username)
        {
            var registered = User.Register(
                Ids.NewId(), username, username, "H:pw", UserType.Employee, [], null, Now);
            Assert.True(registered.IsSuccess);
            await Users.AddAsync(registered.Value!, CancellationToken.None);
            return registered.Value!;
        }

        public FakeCurrentUser As(User user) => new(user.Id);
    }

    private static async Task<Chat> SeedGroupAsync(Ctx ctx, User owner, params User[] members)
    {
        var ids = new List<string> { owner.Id };
        ids.AddRange(members.Select(m => m.Id));
        var handler = new CreateGroupHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(owner));
        var result = await handler.Handle(
            new CreateGroupCommand("Team", ids, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<FileDto> UploadAsync(Ctx ctx, User user, byte[] bytes, string name, string type)
    {
        var handler = new UploadFileHandler(
            ctx.Files, ctx.Storage, ctx.Scanner, ctx.Ids, ctx.Clock, ctx.As(user));
        var result = await handler.Handle(
            new UploadFileCommand(bytes, name, type), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task Upload_stores_bytes_and_metadata()
    {
        var ctx = new Ctx();
        var user = await ctx.SeedUserAsync("up.alice");

        var dto = await UploadAsync(ctx, user, Png, "photo.png", "image/png");

        Assert.Equal("photo.png", dto.FileName);
        Assert.Equal("Image", dto.Kind);
        Assert.Equal(Png.Length, dto.SizeBytes);
        var stored = await ctx.Files.GetByIdAsync(dto.Id, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(user.Id, stored!.OwnerId);
        Assert.False(stored.IsAttached);
        Assert.True(ctx.Storage.Objects.ContainsKey(stored.ObjectKey));
    }

    [Fact]
    public async Task Upload_rejects_content_mismatch()
    {
        var ctx = new Ctx();
        var user = await ctx.SeedUserAsync("up.bob");
        var handler = new UploadFileHandler(
            ctx.Files, ctx.Storage, ctx.Scanner, ctx.Ids, ctx.Clock, ctx.As(user));

        var result = await handler.Handle(
            new UploadFileCommand(Png, "photo.jpg", "image/jpeg"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ContentMismatch, result.Error);
        Assert.Empty(ctx.Storage.Objects);
    }

    [Fact]
    public async Task Upload_rejects_oversize()
    {
        var ctx = new Ctx();
        var user = await ctx.SeedUserAsync("up.carol");
        var handler = new UploadFileHandler(
            ctx.Files, ctx.Storage, ctx.Scanner, ctx.Ids, ctx.Clock, ctx.As(user));

        var big = new byte[FileValidation.MaxImageBytes + 1];
        Png.CopyTo(big, 0);
        var result = await handler.Handle(
            new UploadFileCommand(big, "big.png", "image/png"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.TooLarge, result.Error);
    }

    [Fact]
    public async Task Upload_rejects_infected_and_stores_nothing()
    {
        var ctx = new Ctx();
        var user = await ctx.SeedUserAsync("up.dave");
        ctx.Scanner.Verdict = Application.Abstractions.ScanVerdict.Infected;
        var handler = new UploadFileHandler(
            ctx.Files, ctx.Storage, ctx.Scanner, ctx.Ids, ctx.Clock, ctx.As(user));

        var result = await handler.Handle(
            new UploadFileCommand(Png, "photo.png", "image/png"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ScanRejected, result.Error);
        Assert.Empty(ctx.Storage.Objects);
    }

    [Fact]
    public async Task Upload_maps_storage_outage_to_unavailable()
    {
        var ctx = new Ctx();
        var user = await ctx.SeedUserAsync("up.erin");
        ctx.Storage.FailNext = true;
        var handler = new UploadFileHandler(
            ctx.Files, ctx.Storage, ctx.Scanner, ctx.Ids, ctx.Clock, ctx.As(user));

        var result = await handler.Handle(
            new UploadFileCommand(Png, "photo.png", "image/png"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.StorageUnavailable, result.Error);
    }

    [Fact]
    public async Task Send_with_file_attaches_and_labels_preview()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("media.a");
        var b = await ctx.SeedUserAsync("media.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var upload = await UploadAsync(ctx, a, Png, "photo.png", "image/png");

        var sent = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "", "k-1", null, upload.Id), CancellationToken.None);

        Assert.True(sent.IsSuccess);
        Assert.NotNull(sent.Value!.Attachment);
        Assert.Equal(upload.Id, sent.Value.Attachment!.FileId);
        Assert.Equal("photo.png", sent.Value.Attachment!.FileName);

        var stored = await ctx.Files.GetByIdAsync(upload.Id, CancellationToken.None);
        Assert.Equal(chat.Id, stored!.ChatId);
        Assert.Equal(sent.Value.Id, stored.MessageId);

        var reloaded = await ctx.Chats.GetByIdAsync(chat.Id, CancellationToken.None);
        Assert.Equal("📷 Photo", reloaded!.LastMessagePreview);
    }

    [Fact]
    public async Task Send_rejects_foreign_or_reused_files()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("own.a");
        var b = await ctx.SeedUserAsync("own.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var upload = await UploadAsync(ctx, a, Png, "photo.png", "image/png");

        // Another user's upload is unusable (no existence leak: NotOwner, not NotFound).
        var foreign = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(b))
            .Handle(new SendMessageCommand(chat.Id, "mine?", "k-9", null, upload.Id), CancellationToken.None);
        Assert.True(foreign.IsFailure);
        Assert.Equal(FileErrors.NotOwner, foreign.Error);

        await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "", "k-1", null, upload.Id), CancellationToken.None);

        // The same upload cannot back a second message.
        var reuse = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "", "k-2", null, upload.Id), CancellationToken.None);
        Assert.True(reuse.IsFailure);
        Assert.Equal(FileErrors.AlreadyAttached, reuse.Error);

        // Unknown file id.
        var missing = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "", "k-3", null, "id-999"), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal(FileErrors.NotFound, missing.Error);
    }

    [Fact]
    public async Task Forward_shares_bytes_without_reattaching()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("fwd.a");
        var b = await ctx.SeedUserAsync("fwd.b");
        var src = await SeedGroupAsync(ctx, a, b);
        var dst = await SeedGroupAsync(ctx, a, b);
        var upload = await UploadAsync(ctx, a, Mp4, "clip.mp4", "video/mp4");

        var sent = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(src.Id, "", "k-1", null, upload.Id), CancellationToken.None);
        Assert.True(sent.IsSuccess);

        var fwd = await new ForwardMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new ForwardMessageCommand(src.Id, sent.Value!.Id, dst.Id, "k-fwd"), CancellationToken.None);

        Assert.True(fwd.IsSuccess);
        Assert.NotNull(fwd.Value!.Attachment);
        Assert.Equal(upload.Id, fwd.Value.Attachment!.FileId);

        // Original linkage untouched: bytes still belong to the source message.
        var stored = await ctx.Files.GetByIdAsync(upload.Id, CancellationToken.None);
        Assert.Equal(sent.Value.Id, stored!.MessageId);
    }

    [Fact]
    public async Task Download_authorizes_members_and_uploader_only()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("dl.a");
        var b = await ctx.SeedUserAsync("dl.b");
        var outsider = await ctx.SeedUserAsync("dl.out");
        var chat = await SeedGroupAsync(ctx, a, b);
        var upload = await UploadAsync(ctx, a, Png, "photo.png", "image/png");

        var sent = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "", "k-1", null, upload.Id), CancellationToken.None);
        Assert.True(sent.IsSuccess);

        // Member downloads the exact bytes.
        var memberDl = await new DownloadFileHandler(ctx.Files, ctx.Storage, ctx.Chats, ctx.As(b))
            .Handle(new DownloadFileQuery(upload.Id), CancellationToken.None);
        Assert.True(memberDl.IsSuccess);
        Assert.Equal(Png, memberDl.Value!.Bytes);
        Assert.Equal("image/png", memberDl.Value.ContentType);

        // Outsider learns nothing (neutral NotFound).
        var denied = await new DownloadFileHandler(ctx.Files, ctx.Storage, ctx.Chats, ctx.As(outsider))
            .Handle(new DownloadFileQuery(upload.Id), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(FileErrors.NotFound, denied.Error);
    }

    [Fact]
    public async Task Download_allows_uploader_preview_of_pending_file()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("pv.a");
        var other = await ctx.SeedUserAsync("pv.other");
        var upload = await UploadAsync(ctx, a, Png, "photo.png", "image/png");

        var own = await new DownloadFileHandler(ctx.Files, ctx.Storage, ctx.Chats, ctx.As(a))
            .Handle(new DownloadFileQuery(upload.Id), CancellationToken.None);
        Assert.True(own.IsSuccess);

        var stranger = await new DownloadFileHandler(ctx.Files, ctx.Storage, ctx.Chats, ctx.As(other))
            .Handle(new DownloadFileQuery(upload.Id), CancellationToken.None);
        Assert.True(stranger.IsFailure);
        Assert.Equal(FileErrors.NotFound, stranger.Error);
    }

    [Fact]
    public async Task Delete_message_keeps_file_downloadable()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("keep.owner");
        var member = await ctx.SeedUserAsync("keep.member");
        var chat = await SeedGroupAsync(ctx, owner, member);
        var upload = await UploadAsync(ctx, member, Png, "photo.png", "image/png");

        var sent = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(member))
            .Handle(new SendMessageCommand(chat.Id, "", "k-1", null, upload.Id), CancellationToken.None);
        Assert.True(sent.IsSuccess);

        var deleted = await new DeleteMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(member))
            .Handle(new DeleteMessageCommand(sent.Value!.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);
        Assert.Null(deleted.Value!.Attachment);

        // Retention owns bytes now: still fetchable by members (shared forwards may reference them).
        var after = await new DownloadFileHandler(ctx.Files, ctx.Storage, ctx.Chats, ctx.As(owner))
            .Handle(new DownloadFileQuery(upload.Id), CancellationToken.None);
        Assert.True(after.IsSuccess);
    }

    [Fact]
    public async Task DeleteFile_removes_pending_uploads_only()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("rm.a");
        var b = await ctx.SeedUserAsync("rm.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var pending = await UploadAsync(ctx, a, Png, "photo.png", "image/png");

        // Non-owner cannot delete.
        var forbidden = await new DeleteFileHandler(ctx.Files, ctx.Storage, ctx.As(b))
            .Handle(new DeleteFileCommand(pending.Id), CancellationToken.None);
        Assert.True(forbidden.IsFailure);
        Assert.Equal(FileErrors.NotOwner, forbidden.Error);

        // Owner deletes the pending upload: metadata + bytes gone.
        var removed = await new DeleteFileHandler(ctx.Files, ctx.Storage, ctx.As(a))
            .Handle(new DeleteFileCommand(pending.Id), CancellationToken.None);
        Assert.True(removed.IsSuccess);
        Assert.Null(await ctx.Files.GetByIdAsync(pending.Id, CancellationToken.None));

        // Attached files are immutable via this path.
        var attached = await UploadAsync(ctx, a, Png, "keep.png", "image/png");
        await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "", "k-1", null, attached.Id), CancellationToken.None);
        var blocked = await new DeleteFileHandler(ctx.Files, ctx.Storage, ctx.As(a))
            .Handle(new DeleteFileCommand(attached.Id), CancellationToken.None);
        Assert.True(blocked.IsFailure);
        Assert.Equal(FileErrors.AlreadyAttached, blocked.Error);
    }

    [Fact]
    public async Task History_enriches_attachments_in_one_pass()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("hist.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("hist.b"));
        var upload = await UploadAsync(ctx, a, Png, "photo.png", "image/png");
        await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new SendMessageCommand(chat.Id, "look", "k-1", null, upload.Id), CancellationToken.None);

        var page = await new GetHistoryHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.As(a))
            .Handle(new GetHistoryQuery(chat.Id, null, 10), CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Single(page.Value!.Items);
        Assert.NotNull(page.Value.Items[0].Attachment);
        Assert.Equal($"/api/v1/files/{upload.Id}", page.Value.Items[0].Attachment!.Url);
    }
}
