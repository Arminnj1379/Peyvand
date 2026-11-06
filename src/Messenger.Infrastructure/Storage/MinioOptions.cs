using System.ComponentModel.DataAnnotations;

namespace Messenger.Infrastructure.Storage;

/// <summary>S3-compatible object storage settings. Bound from the <c>Minio</c> config section.</summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    [Required]
    public string Endpoint { get; set; } = "localhost:9000";

    [Required]
    public string AccessKey { get; set; } = "minioadmin";

    [Required]
    public string SecretKey { get; set; } = "minioadmin";

    [Required]
    public string Bucket { get; set; } = "messenger-media";

    public bool UseSSL { get; set; } = false;
}
