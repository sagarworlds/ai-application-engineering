using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Infrastructure.Llm;
using KnowledgeAssistant.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Infrastructure;

/// <summary>Registers infrastructure adapters (persistence, model providers).</summary>
public static class DependencyInjection
{
    /// <summary>Name of the connection string for the knowledge store.</summary>
    public const string ConnectionStringName = "KnowledgeDb";

    /// <summary>Adds infrastructure services to the container.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is missing.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured (ConnectionStrings:{ConnectionStringName}).");
        }

        services.AddDbContext<KnowledgeDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IConversationStore, EfConversationStore>();
        services.AddScoped<IChunkRetriever, EmptyChunkRetriever>();

        services.AddOptions<LlmOptions>()
            .BindConfiguration(LlmOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();

        // Typed clients: IHttpClientFactory owns handler lifetime (DNS refresh, socket reuse).
        services.AddHttpClient<IChatModel, OpenAiCompatibleChatModel>(ConfigureLlmClient);

        services.AddHealthChecks().AddDbContextCheck<KnowledgeDbContext>("knowledge-db", tags: [ReadinessTag]);

        return services;
    }

    private static void ConfigureLlmClient(IServiceProvider services, HttpClient client) =>
        LlmHttp.Configure(client, services.GetRequiredService<IOptions<LlmOptions>>().Value);

    /// <summary>Health-check tag for dependencies that must be reachable before serving traffic.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Creates the development database if it does not exist. Production stores are expected to
    /// be provisioned by migrations/infrastructure code instead (see backlog).
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task InitializeKnowledgeStoreAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();

        // SQLite creates the file but not its parent directory.
        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
    }
}
