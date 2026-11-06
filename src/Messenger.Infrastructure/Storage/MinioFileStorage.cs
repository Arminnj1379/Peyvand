using Messenger.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Messenger.Infrastructure.Storage;

/// <summary>
/// S3-compatible <see cref="IFileStorage"/> over MinIO. Binaries stream here;
/// metadata stays in MongoDB. Single shared client (thread-safe); bucket is
/// ensured at startup. Object keys are server-generated and unguessable —
/// all authorization happens in the API before touching this port.
/// </summary>
public sealed class MinioFileStorage : IFileStorage
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly ILogger<MinioFileStorage> _logger;

    public MinioFileStorage(IOptions<MinioOptions> options, ILogger<MinioFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        var value = options.Value;
        _bucket = value.Bucket;
        _client = new MinioClient()
            .WithEndpoint(value.Endpoint)
            .WithCredentials(value.AccessKey, value.SecretKey)
            .WithSSL(value.UseSSL)
            .Build();
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucket), ct).ConfigureAwait(false);
        if (!exists)
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_bucket), ct).ConfigureAwait(false);
            _logger.LogInformation("Created object storage bucket {Bucket}.", _bucket);
        }
    }

    public async Task PutAsync(string objectKey, string contentType, byte[] bytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(bytes.Length)
            .WithContentType(contentType), ct).ConfigureAwait(false);
    }

    public async Task<(byte[] Bytes, string ContentType, long SizeBytes)?> GetAsync(string objectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            using var output = new MemoryStream();
            var stat = await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithCallbackStream(async (stream, token) =>
                {
                    await stream.CopyToAsync(output, token).ConfigureAwait(false);
                }), ct).ConfigureAwait(false);
            return (output.ToArray(), stat.ContentType, stat.Size);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey), ct).ConfigureAwait(false);
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            // Already gone — the metadata delete is the source of truth.
        }
    }
}
