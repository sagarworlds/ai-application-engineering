using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Documents;
using KnowledgeAssistant.Application.Questions;

using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAssistant.Application;

/// <summary>Registers the application layer's use cases.</summary>
public static class DependencyInjection
{
    /// <summary>Adds application services to the container.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<DocumentQueryService>();
        services.AddScoped<IQuestionAnswerer, QuestionAnsweringService>();
        return services;
    }
}
