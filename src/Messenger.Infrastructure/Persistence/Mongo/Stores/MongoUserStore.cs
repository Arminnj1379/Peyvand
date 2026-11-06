using Messenger.Application.Abstractions;
using Messenger.Domain.Users;using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="IUserStore"/> (collection <c>users</c>). Driver calls directly, no abstractions.</summary>
public sealed class MongoUserStore(MongoDbContext context) : IUserStore
{
    public const string CollectionName = "users";

    private IMongoCollection<UserDocument> Collection => context.Collection<UserDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        await keys.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(d => d.Username),
                new CreateIndexOptions { Unique = true, Name = "ux_username" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(d => d.Phone),
                new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_phone" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Text(d => d.DisplayName).Text(d => d.Username),
                new CreateIndexOptions { Name = "txt_display_username" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(d => d.Roles),
                new CreateIndexOptions { Name = "ix_roles" }),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : UserMapper.ToDomain(doc);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        var doc = await Collection.Find(d => d.Username == username).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : UserMapper.ToDomain(doc);
    }

    public async Task<User?> GetByPhoneAsync(string phone, CancellationToken ct)
    {
        var doc = await Collection.Find(d => d.Phone == phone).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : UserMapper.ToDomain(doc);
    }

    public async Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct) =>
        await Collection.Find(d => d.Username == username).AnyAsync(ct).ConfigureAwait(false);

    public async Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct) =>
        await Collection.Find(d => d.Phone == phone).AnyAsync(ct).ConfigureAwait(false);

    public async Task<bool> AnyAdministratorAsync(CancellationToken ct) =>
        await Collection
            .Find(d => d.Roles.Contains(nameof(Role.SystemAdministrator)))
            .AnyAsync(ct).ConfigureAwait(false);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        try
        {
            await Collection.InsertOneAsync(UserMapper.ToDocument(user), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw DuplicateException(ex);
        }
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        try
        {
            var result = await Collection
                .ReplaceOneAsync(d => d.Id == user.Id, UserMapper.ToDocument(user), cancellationToken: ct)
                .ConfigureAwait(false);
            if (result.MatchedCount == 0)
            {
                throw new InvalidOperationException($"User {user.Id} does not exist.");
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw DuplicateException(ex);
        }
    }

    public async Task<IReadOnlyList<User>> SearchAsync(string query, bool includeCustomers, int limit, CancellationToken ct)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(query);
        var filter = Builders<UserDocument>.Filter.And(
            Builders<UserDocument>.Filter.Or(
                Builders<UserDocument>.Filter.Regex(d => d.Username, new BsonRegularExpression(escaped, "i")),
                Builders<UserDocument>.Filter.Regex(d => d.DisplayName, new BsonRegularExpression(escaped, "i"))),
            includeCustomers
                ? Builders<UserDocument>.Filter.Empty
                : Builders<UserDocument>.Filter.Eq(d => d.UserType, Domain.Users.UserType.Employee));

        var docs = await Collection.Find(filter).SortBy(d => d.Username).Limit(limit).ToListAsync(ct)
            .ConfigureAwait(false);
        return docs.Select(UserMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<User>> ListAsync(
        string? userType, string? role, bool? isActive, string? cursor, int limit, CancellationToken ct)
    {
        var builder = Builders<UserDocument>.Filter;
        var filters = new List<FilterDefinition<UserDocument>>();

        if (Enum.TryParse<Domain.Users.UserType>(userType, ignoreCase: true, out var parsedType))
        {
            filters.Add(builder.Eq(d => d.UserType, parsedType));
        }

        if (Enum.TryParse<Role>(role, ignoreCase: true, out var parsedRole))
        {
            filters.Add(builder.AnyEq(d => d.Roles, parsedRole.ToString()));
        }

        if (isActive.HasValue)
        {
            filters.Add(builder.Eq(d => d.IsActive, isActive.Value));
        }

        if (cursor is not null)
        {
            // Fail closed: a malformed cursor returns nothing instead of
            // silently restarting pagination from the beginning (client loop).
            if (!ObjectId.TryParse(cursor, out _))
            {
                return [];
            }

            filters.Add(builder.Gt(d => d.Id, cursor));
        }

        var filter = filters.Count == 0
            ? builder.Empty
            : builder.And(filters);
        var docs = await Collection.Find(filter).SortBy(d => d.Id).Limit(limit).ToListAsync(ct)
            .ConfigureAwait(false);
        return docs.Select(UserMapper.ToDomain).ToList();
    }

    private static DuplicateUserException DuplicateException(MongoWriteException ex)
    {
        var message = ex.Message;
        return message.Contains("ux_phone", StringComparison.OrdinalIgnoreCase)
            || message.Contains("phone_", StringComparison.OrdinalIgnoreCase)
            ? new DuplicateUserException(Domain.Users.UserErrors.PhoneTaken)
            : new DuplicateUserException(Domain.Users.UserErrors.UsernameTaken);
    }

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        // ObjectId-typed field: garbage strings would throw at serialization,
        // so only well-formed ids reach the $in filter (callers treat the
        // missing remainder as unknown/deleted).
        var valid = ids.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (valid.Count == 0)
        {
            return [];
        }

        var docs = await Collection.Find(d => valid.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(UserMapper.ToDomain).ToList();
    }
}
