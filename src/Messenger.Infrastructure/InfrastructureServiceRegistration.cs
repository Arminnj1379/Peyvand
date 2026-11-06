using Messenger.Application.Abstractions;
using Messenger.Infrastructure.Auth;
using Messenger.Infrastructure.Ids;
using Messenger.Infrastructure.Persistence.Mongo;
using Messenger.Infrastructure.Persistence.Mongo.Stores;
using Messenger.Infrastructure.Push;
using Messenger.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Messenger.Infrastructure;

/// <summary>Wires Infrastructure adapters. Called once from the API composition root.</summary>
public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>()
            .Bind(configuration.GetSection(MongoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SeedAdminOptions>()
            .Bind(configuration.GetSection(SeedAdminOptions.SectionName))
            .Validate(
                o => !o.Enabled || (!string.IsNullOrWhiteSpace(o.Username) && !string.IsNullOrWhiteSpace(o.Password)),
                "SeedAdmin requires Username and Password when Enabled.")
            .ValidateOnStart();

        services.AddOptions<Storage.FileStorageOptions>()
            .Bind(configuration.GetSection(Storage.FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<Storage.MinioOptions>()
            .Bind(configuration.GetSection(Storage.MinioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // LiveKit keys are optional at boot: text chat must work without the SFU.
        // Join fails closed (503) while signaling keeps working.
        services.AddOptions<Calls.LiveKitOptions>()
            .Bind(configuration.GetSection(Calls.LiveKitOptions.SectionName));

        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });
        services.AddSingleton<MongoDbContext>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, MongoObjectIdGenerator>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IPushSender, NoOpPushSender>();

        services.AddSingleton<IAuthTokenService, JwtTokenService>();
        services.AddSingleton<MongoUserStore>();
        services.AddSingleton<MongoDeviceSessionStore>();
        services.AddSingleton<MongoAvatarStorage>();
        services.AddSingleton<MongoChatStore>();
        services.AddSingleton<MongoMessageStore>();
        services.AddSingleton<MongoFileStore>();
        services.AddSingleton<MongoCallStore>();
        services.AddSingleton<MongoTicketStore>();
        services.AddSingleton<ILiveKitTokenService, Calls.LiveKitTokenService>();
        services.AddSingleton<IUserStore>(sp => sp.GetRequiredService<MongoUserStore>());
        services.AddSingleton<IDeviceSessionStore>(sp => sp.GetRequiredService<MongoDeviceSessionStore>());
        services.AddSingleton<IAvatarStorage>(sp => sp.GetRequiredService<MongoAvatarStorage>());
        services.AddSingleton<IChatStore>(sp => sp.GetRequiredService<MongoChatStore>());
        services.AddSingleton<IMessageStore>(sp => sp.GetRequiredService<MongoMessageStore>());
        services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<MongoFileStore>());
        services.AddSingleton<ISupportTicketStore>(sp => sp.GetRequiredService<MongoTicketStore>());
        services.AddSingleton<ICallStore>(sp => sp.GetRequiredService<MongoCallStore>());
        services.AddSingleton<IFileScanner, Storage.NoOpFileScanner>();

        // MinIO is the only Production backend. Filesystem is a Development
        // convenience for machines without a MinIO container (same interface).
        var storageProvider = configuration.GetValue<string>(
            Storage.FileStorageOptions.SectionName + ":Provider");
        if (string.Equals(storageProvider, "Filesystem", StringComparison.OrdinalIgnoreCase))
        {
            var root = configuration.GetValue<string>(
                Storage.FileStorageOptions.SectionName + ":RootPath");
            services.AddSingleton<IFileStorage>(
                new Storage.LocalFileStorage(string.IsNullOrWhiteSpace(root) ? "data/media" : root));
            services.AddHealthChecks().AddCheck<Storage.LocalStorageHealthCheck>(
                "local-storage", tags: ["storage"]);
        }
        else
        {
            services.AddSingleton<IFileStorage, Storage.MinioFileStorage>();
            services.AddHealthChecks().AddCheck<Storage.MinioHealthCheck>("minio", tags: ["storage"]);
        }

        services.AddHostedService<MongoInitializer>();
        services.AddHostedService<AdminSeeder>();

        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongo", tags: ["ready"]);

        return services;
    }
}
