using Messenger.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Messenger.Api.Tests;

/// <summary>
/// MinIO adapter round-trip against a live server. Skipped (pass) wherever
/// MinIO is unreachable — API behavior is covered by <c>MediaFlowTests</c>
/// with same-semantics in-memory storage; this guards the thin S3 mapping.
/// Run with MinIO up (<c>docker compose up -d minio</c>) to exercise it.
/// </summary>
public sealed class MinioFileStorageLiveTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    private static async Task<bool> MinioReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var res = await http.GetAsync("http://localhost:9000/minio/health/live");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static MinioFileStorage CreateStorage(string bucket) =>
        new(Options.Create(new MinioOptions
        {
            Endpoint = "localhost:9000",
            AccessKey = "minioadmin",
            SecretKey = "minioadmin",
            Bucket = bucket,
            UseSSL = false
        }), NullLogger<MinioFileStorage>.Instance);

    [Fact]
    public async Task Put_get_delete_round_trip()
    {
        if (!await MinioReachableAsync())
        {
            return;
        }

        var bucket = $"messenger-media-tests-{Guid.NewGuid():n}"[..32];
        var storage = CreateStorage(bucket);
        await storage.EnsureBucketAsync(CancellationToken.None);
        await storage.EnsureBucketAsync(CancellationToken.None); // idempotent

        const string key = "files/test-id/photo.png";
        await storage.PutAsync(key, "image/png", Png, CancellationToken.None);

        var got = await storage.GetAsync(key, CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal(Png, got!.Value.Bytes);
        Assert.Equal("image/png", got.Value.ContentType);

        await storage.DeleteAsync(key, CancellationToken.None);
        Assert.Null(await storage.GetAsync(key, CancellationToken.None));

        // Missing keys read as null, delete as no-op.
        Assert.Null(await storage.GetAsync("files/nope/missing.png", CancellationToken.None));
        await storage.DeleteAsync("files/nope/missing.png", CancellationToken.None);
    }
}
