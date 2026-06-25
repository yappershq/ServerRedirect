using Microsoft.Extensions.DependencyInjection;
using ServerRedirect.Configuration;
using ServerRedirect.Modules;

namespace ServerRedirect;

internal static class ModuleDependencyInjection
{
    internal static IServiceCollection AddModules(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
            ServerRedirectConfig.Load(
                sp.GetRequiredService<InterfaceBridge>().SharpPath,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("ServerRedirectConfig")));

        services.AddSingleton<ServerRedirectModule>();
        services.AddSingleton<IModule>(sp => sp.GetRequiredService<ServerRedirectModule>());
        return services;
    }
}
