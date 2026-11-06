using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class AvatarFlowTests(MessengerFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Minimal but well-formed PNG (signature + IHDR length prefix).
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x01, 0x02, 0x03, 0x04
    ];

    private async Task<HttpClient> AuthedClientAsync()
    {
        await factory.CleanAsync();
        var client = factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "avatar.user",
            displayName = "Avatar",
            password = "password123",
            phone = (string?)null,
            deviceLabel = (string?)null
        });
        var auth = (await reg.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static MultipartFormDataContent PngContent(byte[] bytes, string contentType = "image/png")
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(part, "file", "avatar.png");
        return content;
    }

    [Fact]
    public async Task Upload_get_remove_round_trip()
    {
        var client = await AuthedClientAsync();

        var upload = await client.PostAsync("/api/v1/users/me/avatar", PngContent(Png));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var me = (await upload.Content.ReadFromJsonAsync<UserResponse>(Json))!;
        Assert.NotNull(me.AvatarUrl);

        var get = await client.GetAsync(me.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await get.Content.ReadAsByteArrayAsync());

        var remove = await client.DeleteAsync("/api/v1/users/me/avatar");
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        var gone = await client.GetAsync(me.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Forged_image_is_rejected()
    {
        var client = await AuthedClientAsync();
        var res = await client.PostAsync(
            "/api/v1/users/me/avatar",
            PngContent("this is not an image at all, honestly"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Oversized_image_is_rejected()
    {
        var client = await AuthedClientAsync();
        var big = new byte[2 * 1024 * 1024 + 1];
        big[0] = 0x89;
        big[1] = 0x50;
        var res = await client.PostAsync("/api/v1/users/me/avatar", PngContent(big));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
