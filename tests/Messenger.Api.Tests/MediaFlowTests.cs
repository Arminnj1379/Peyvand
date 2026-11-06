using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class MediaFlowTests(MessengerFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
    private static readonly byte[] Mp4 =
        [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D, 0x01];

    private async Task<HttpClient> CleanClientAsync()
    {
        await factory.CleanAsync();
        return factory.CreateClient();
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username)
    {
        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            displayName = username,
            password = "password123",
            phone = (string?)null,
            deviceLabel = "test"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    private static void AuthAs(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static MultipartFormDataContent UploadContent(byte[] bytes, string name, string contentType)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", name);
        return form;
    }

    private static async Task<JsonElement> UploadAsync(HttpClient client, byte[] bytes, string name, string type)
    {
        using var form = UploadContent(bytes, name, type);
        var res = await client.PostAsync("/api/v1/files", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement;
    }

    [Fact]
    public async Task Upload_send_history_download_flow()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "media.a");
        var b = await RegisterAsync(factory.CreateClient(), "media.b");

        AuthAs(client, a.AccessToken);
        var meta = await UploadAsync(client, Png, "photo.png", "image/png");
        Assert.Equal("photo.png", meta.GetProperty("fileName").GetString());
        Assert.Equal("Image", meta.GetProperty("kind").GetString());
        var fileId = meta.GetProperty("id").GetString()!;

        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        // Caption-less media message.
        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "", clientMessageId = "m-1", replyToMessageId = (string?)null, fileId });
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var msg = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var attachment = msg.RootElement.GetProperty("attachment");
        Assert.Equal(fileId, attachment.GetProperty("fileId").GetString());
        Assert.Equal("photo.png", attachment.GetProperty("fileName").GetString());

        // History enriches the attachment; chat preview falls back to a label.
        var hist = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10");
        var page = (await hist.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var item = page.RootElement.GetProperty("items")[0];
        Assert.Equal(fileId, item.GetProperty("attachment").GetProperty("fileId").GetString());
        var chats = await client.GetAsync("/api/v1/chats?limit=10");
        var list = (await chats.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal("📷 Photo", list.RootElement[0].GetProperty("lastMessagePreview").GetString());

        // Both members download the exact bytes.
        var dl = await client.GetAsync($"/api/v1/files/{fileId}");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal("image/png", dl.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await dl.Content.ReadAsByteArrayAsync());

        var bClient = factory.CreateClient();
        AuthAs(bClient, b.AccessToken);
        var dlB = await bClient.GetAsync($"/api/v1/files/{fileId}");
        Assert.Equal(HttpStatusCode.OK, dlB.StatusCode);
    }

    [Fact]
    public async Task Outsider_download_and_send_are_denied()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "priv.a");
        var b = await RegisterAsync(factory.CreateClient(), "priv.b");
        var c = await RegisterAsync(factory.CreateClient(), "priv.c");

        AuthAs(client, a.AccessToken);
        var meta = await UploadAsync(client, Png, "photo.png", "image/png");
        var fileId = meta.GetProperty("id").GetString()!;

        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        var cClient = factory.CreateClient();
        AuthAs(cClient, c.AccessToken);

        // No existence leak: 404, not 403.
        Assert.Equal(HttpStatusCode.NotFound, (await cClient.GetAsync($"/api/v1/files/{fileId}")).StatusCode);

        // Someone else's upload cannot back my message.
        var send = await cClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "", clientMessageId = "k-x", replyToMessageId = (string?)null, fileId });
        Assert.True(send.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_validation_rejects_bad_files()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "val.a");
        AuthAs(client, a.AccessToken);

        // PNG bytes wearing a JPEG name.
        using (var form = UploadContent(Png, "photo.jpg", "image/jpeg"))
        {
            var res = await client.PostAsync("/api/v1/files", form);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // Unsupported extension.
        using (var form = UploadContent(Png, "run.exe", "application/x-msdownload"))
        {
            var res = await client.PostAsync("/api/v1/files", form);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // Oversize (11 MB PNG).
        var big = new byte[11 * 1024 * 1024];
        Png.CopyTo(big, 0);
        using (var form = UploadContent(big, "big.png", "image/png"))
        {
            var res = await client.PostAsync("/api/v1/files", form);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        }
    }

    [Fact]
    public async Task Delete_message_keeps_file_but_deletes_drop_pending()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "del.a");
        var b = await RegisterAsync(factory.CreateClient(), "del.b");

        AuthAs(client, a.AccessToken);
        var meta = await UploadAsync(client, Mp4, "clip.mp4", "video/mp4");
        var fileId = meta.GetProperty("id").GetString()!;

        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "watch", clientMessageId = "m-1", replyToMessageId = (string?)null, fileId });
        var msg = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var msgId = msg.RootElement.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/messages/{msgId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/files/{fileId}")).StatusCode);

        // Attached files cannot be deleted directly.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/files/{fileId}")).StatusCode);

        // Pending uploads can: upload, delete, then gone.
        var pending = await UploadAsync(client, Png, "tmp.png", "image/png");
        var pendingId = pending.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/files/{pendingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/files/{pendingId}")).StatusCode);

        // Non-owners cannot delete pending uploads.
        var other = await UploadAsync(client, Png, "tmp2.png", "image/png");
        var otherId = other.GetProperty("id").GetString()!;
        var bClient = factory.CreateClient();
        AuthAs(bClient, b.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await bClient.DeleteAsync($"/api/v1/files/{otherId}")).StatusCode);
    }
}
