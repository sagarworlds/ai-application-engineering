using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Application.Common;

/// <summary>The one way options classes are registered in this solution.</summary>
public static class OptionsRegistrationExtensions
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to a configuration section, validates its data
    /// annotations and does so at startup: a misconfigured deployment fails immediately instead of
    /// on the first request that happens to read the setting.
    /// </summary>
    /// <typeparam name="TOptions">Options class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="sectionName">Configuration section to bind.</param>
    /// <returns>The options builder, for further validation.</returns>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services, string sectionName)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
