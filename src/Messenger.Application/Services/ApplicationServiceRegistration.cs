using Messenger.Application.Services.Implementations;
using Messenger.Application.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Application.Services;

/// <summary>
/// Composition root for the Application layer: conventional scoped services
/// plus FluentValidation validators (invoked inline by each service).
/// No MediatR — controllers call these services directly.
/// </summary>
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IChatInteractionService, ChatInteractionService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<ICallService, CallService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<ISupportService, SupportService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISystemService, SystemService>();
        return services;
    }
}
