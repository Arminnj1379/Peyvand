using System.ComponentModel.DataAnnotations;

namespace Messenger.Infrastructure.Persistence.Mongo;

/// <summary>MongoDB connection settings. Bound from <c>Mongo</c> config section.</summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    [Required]
    public string ConnectionString { get; set; } = "mongodb://localhost:27017?directConnection=true";

    [Required]
    public string DatabaseName { get; set; } = "messenger";
}
