using KnowledgeAssistant.Domain.Security;

namespace KnowledgeAssistant.Domain.Documents;

/// <summary>Metadata of an ingested document. The text itself lives in its chunks.</summary>
public sealed class Document
{
    /// <summary>Maximum length of <see cref="FileName"/> and <see cref="Title"/>.</summary>
    public const int MaxNameLength = 255;

    // Required by EF Core materialisation.
    private Document()
    {
    }

    /// <summary>Unique identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant that owns the document. Immutable after creation.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Original file name, used in citations.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Human-readable title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the extracted content; used to reject duplicate ingestion per tenant.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>Number of pages supplied at ingestion.</summary>
    public int PageCount { get; private set; }

    /// <summary>Number of chunks stored for the document.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>Identifier of the user who ingested the document.</summary>
    public string UploadedByUserId { get; private set; } = string.Empty;

    /// <summary>
    /// Roles allowed to read the document. Empty means every member of the tenant may read it.
    /// </summary>
    public IReadOnlyList<string> AllowedRoles { get; private set; } = [];

    /// <summary>Creation time (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Creates a new document owned by <paramref name="owner"/>'s tenant.</summary>
    /// <param name="owner">The caller ingesting the document; supplies tenant and uploader.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="title">Display title; falls back to <paramref name="fileName"/> when blank.</param>
    /// <param name="contentHash">SHA-256 hex digest of the content.</param>
    /// <param name="pageCount">Number of pages (at least 1).</param>
    /// <param name="chunkCount">Number of chunks (zero or more).</param>
    /// <param name="allowedRoles">Optional role restriction.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    /// <returns>The new document.</returns>
    /// <exception cref="ArgumentException">A required value is missing or too long.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is out of range.</exception>
    public static Document Create(
        UserContext owner,
        string fileName,
        string? title,
        string contentHash,
        int pageCount,
        int chunkCount,
        IEnumerable<string>? allowedRoles,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);

        var effectiveTitle = string.IsNullOrWhiteSpace(title) ? fileName : title.Trim();
        if (fileName.Length > MaxNameLength || effectiveTitle.Length > MaxNameLength)
        {
            throw new ArgumentException($"File name and title must be at most {MaxNameLength} characters.");
        }

        return new Document
        {
            Id = Guid.NewGuid(),
            TenantId = owner.TenantId,
            UploadedByUserId = owner.UserId,
            FileName = fileName.Trim(),
            Title = effectiveTitle,
            ContentHash = contentHash,
            PageCount = pageCount,
            ChunkCount = chunkCount,
            AllowedRoles = NormalizeRoles(allowedRoles),
            // Millisecond precision: what the store keeps, so a created entity equals its reloaded self.
            CreatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(createdAtUtc.ToUnixTimeMilliseconds()),
        };
    }

    /// <summary>
    /// The access rule for a document. A caller may read it only when they are in the same tenant
    /// and either the document is unrestricted, they hold one of the allowed roles, or they are a
    /// tenant admin. Being an admin never crosses the tenant boundary.
    /// </summary>
    /// <param name="user">The caller.</param>
    /// <returns>True when the caller may read the document.</returns>
    public bool IsAccessibleTo(UserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!string.Equals(TenantId, user.TenantId, StringComparison.Ordinal))
        {
            return false;
        }

        return AllowedRoles.Count == 0 || user.IsTenantAdmin || AllowedRoles.Any(user.Roles.Contains);
    }

    private static List<string> NormalizeRoles(IEnumerable<string>? roles) =>
        roles is null
            ? []
            : [.. roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.Ordinal)];
}
