using System.ComponentModel.DataAnnotations;

namespace Messenger.Infrastructure.Storage;

/// <summary>
/// Selects the <see cref="Messenger.Application.Abstractions.IFileStorage"/>
/// backend. <c>Minio</c> is the only Production backend; <c>Filesystem</c> is a
/// Development convenience for machines without a MinIO container (local disk,
/// same interface, metadata still in MongoDB).
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary><c>Minio</c> (default) or <c>Filesystem</c>.</summary>
    [Required]
    public string Provider { get; set; } = "Minio";

    /// <summary>Root directory for the Filesystem provider (relative = under the API binaries).</summary>
    [Required]
    public string RootPath { get; set; } = "data/media";
}
