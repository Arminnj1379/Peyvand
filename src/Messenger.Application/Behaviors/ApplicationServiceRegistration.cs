using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Application.Behaviors;

/// <summary>Registers MediatR handlers + FluentValidation + pipeline behaviors.</summary>
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceRegistration).Assembly));
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceRegistration).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
