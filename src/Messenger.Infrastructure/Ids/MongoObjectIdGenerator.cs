using Messenger.Application.Abstractions;
using MongoDB.Bson;

namespace Messenger.Infrastructure.Ids;

/// <summary>Storage identity generator (ObjectId strings, per database-design.md).</summary>
public sealed class MongoObjectIdGenerator : IIdGenerator
{
    public string NewId() => ObjectId.GenerateNewId().ToString();
}
