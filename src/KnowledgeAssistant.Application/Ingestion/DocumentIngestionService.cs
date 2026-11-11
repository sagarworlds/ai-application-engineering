using System.Security.Cryptography;
using System.Text;

using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Exceptions;
using KnowledgeAssistant.Domain.Documents;
using KnowledgeAssistant.Domain.Security;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Application.Ingestion;

/// <summary>A request to ingest extracted document content.</summary>
/// <param name="FileName">Original file name.</param>
/// <param name="Title">Display title; defaults to the file name.</param>
/// <param name="Pages">Extracted text per page.</param>
/// <param name="AllowedRoles">Optional roles the document is restricted to.</param>
public sealed record IngestDocumentCommand(
    string FileName,
    string? Title,
    IReadOnlyList<PageContent> Pages,
    IReadOnlyList<string>? AllowedRoles);

/// <summary>
/// Ingestion pipeline: validate → hash (dedupe) → chunk → embed → store. The document and its
/// chunks are written in one transaction, so a failed ingestion leaves nothing behind.
/// </summary>
public sealed partial class DocumentIngestionService(
    ITextChunker chunker,
    IEmbeddingGenerator embeddings,
    IDocumentStore store,
    TimeProvider timeProvider,
    IOptions<IngestionOptions> options,
    ILogger<DocumentIngestionService> logger)
{
    private const int MaxRoleLength = 128;
    private const int MaxAllowedRoles = 20;

    private readonly IngestionOptions _options = options.Value;

    /// <summary>Ingests a document into the caller's tenant.</summary>
    /// <param name="user">The caller; becomes the document's tenant and uploader.</param>
    /// <param name="command">The content to ingest.</param>
    /// <param name="cancellationToken">Cancels the operation; nothing is stored when cancelled.</param>
    /// <returns>The stored document's metadata.</returns>
    /// <exception cref="RequestValidationException">The content violates an ingestion limit or contains no text.</exception>
    /// <exception cref="ConflictException">The same content already exists in the caller's tenant.</exception>
    /// <exception cref="LlmUnavailableException">The embedding model is unavailable or returned unusable vectors.</exception>
    /// <exception cref="LlmTimeoutException">The embedding model timed out.</exception>
    public async Task<Document> IngestAsync(
        UserContext user, IngestDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(command);

        var pages = ValidateAndOrderPages(command);
        ValidateRoles(command.AllowedRoles);

        var contentHash = ComputeContentHash(pages);

        // Deliberately tenant-wide, ignoring the role ACL: content is stored once per tenant. A
        // contributor can therefore learn that identical text already exists in their own tenant
        // even if they cannot read that document — accepted, since they already hold the text.
        if (await store.ContentExistsAsync(user.TenantId, contentHash, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("A document with identical content already exists.");
        }

        var textChunks = chunker.Chunk(pages);
        if (textChunks.Count == 0)
        {
            throw new RequestValidationException("pages", "The pages contain no text to ingest.");
        }

        if (textChunks.Count > _options.MaxChunksPerDocument)
        {
            throw new RequestValidationException(
                "pages", $"The document splits into more than {_options.MaxChunksPerDocument} chunks. Split it into smaller documents.");
        }

        var document = Document.Create(
            user, command.FileName, command.Title, contentHash, pages.Count, textChunks.Count,
            command.AllowedRoles, timeProvider.GetUtcNow());

        var vectors = await EmbedAsync(document.Title, textChunks, cancellationToken).ConfigureAwait(false);
        var chunks = textChunks
            .Select((chunk, i) => DocumentChunk.Create(
                document, chunk.Index, chunk.PageNumber, chunk.Section, chunk.Content, vectors[i], embeddings.ModelId))
            .ToList();

        await store.AddAsync(document, chunks, cancellationToken).ConfigureAwait(false);

        LogIngested(logger, document.Id, document.PageCount, document.ChunkCount, embeddings.ModelId);
        return document;
    }

    private List<PageContent> ValidateAndOrderPages(IngestDocumentCommand command)
    {
        if (command.Pages.Count == 0)
        {
            throw new RequestValidationException("pages", "At least one page is required.");
        }

        if (command.Pages.Any(p => p?.Text is null))
        {
            throw new RequestValidationException("pages", "Pages must not be null and must have text.");
        }

        if (command.Pages.Count > _options.MaxPages)
        {
            throw new RequestValidationException("pages", $"A document may have at most {_options.MaxPages} pages.");
        }

        if (command.Pages.Select(p => p.PageNumber).Distinct().Count() != command.Pages.Count)
        {
            throw new RequestValidationException("pages", "Page numbers must be unique.");
        }

        // long: the sum of many large pages must not overflow before it is compared with the limit.
        var totalCharacters = command.Pages.Sum(p => (long)p.Text.Length);
        if (totalCharacters > _options.MaxTotalCharacters)
        {
            throw new RequestValidationException(
                "pages", $"A document may contain at most {_options.MaxTotalCharacters} characters in total.");
        }

        return [.. command.Pages.OrderBy(p => p.PageNumber)];
    }

    private static void ValidateRoles(IReadOnlyList<string>? roles)
    {
        if (roles is null)
        {
            return;
        }

        // A blank entry must be rejected, not dropped: dropping it could leave the list empty,
        // which means "unrestricted" — the opposite of what the caller asked for.
        if (roles.Any(string.IsNullOrWhiteSpace))
        {
            throw new RequestValidationException("allowedRoles", "Role names must not be blank.");
        }

        if (roles.Count > MaxAllowedRoles || roles.Any(r => r.Length > MaxRoleLength))
        {
            throw new RequestValidationException(
                "allowedRoles", $"At most {MaxAllowedRoles} roles of at most {MaxRoleLength} characters are allowed.");
        }
    }

    private static string ComputeContentHash(List<PageContent> pages)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var page in pages)
        {
            // The page number and a separator are hashed too, so moving text between pages changes the hash.
            hash.AppendData(Encoding.UTF8.GetBytes($"{page.PageNumber}\u001f"));
            hash.AppendData(Encoding.UTF8.GetBytes(page.Text));
            hash.AppendData([0x1e]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private async Task<List<float[]>> EmbedAsync(
        string title, IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>(chunks.Count);
        foreach (var batch in chunks.Chunk(_options.EmbeddingBatchSize))
        {
            var inputs = batch.Select(chunk => EmbeddingText.ForChunk(title, chunk.Section, chunk.Content)).ToList();
            var result = await embeddings.GenerateAsync(inputs, cancellationToken).ConfigureAwait(false);
            if (result.Count != batch.Length)
            {
                throw new LlmUnavailableException("The embedding model returned an unexpected number of vectors.");
            }

            vectors.AddRange(result);
        }

        // Vectors of differing size cannot be compared; refuse to store a document that mixes them.
        if (vectors.Any(v => v.Length == 0 || v.Length != vectors[0].Length))
        {
            throw new LlmUnavailableException("The embedding model returned vectors of inconsistent size.");
        }

        return vectors;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ingested document {DocumentId}: {PageCount} pages, {ChunkCount} chunks, embedding model {EmbeddingModel}")]
    private static partial void LogIngested(
        ILogger logger, Guid documentId, int pageCount, int chunkCount, string embeddingModel);
}
