using KnowledgeAssistant.Domain.Security;
using KnowledgeAssistant.UnitTests.TestSupport;

namespace KnowledgeAssistant.UnitTests.Domain;

public sealed class DocumentAccessTests
{
    [Fact]
    public void IsAccessibleTo_UnrestrictedDocumentSameTenant_ReturnsTrue()
    {
        var document = TestDocuments.Create(TestUsers.Contributor("tenant-a"));

        Assert.True(document.IsAccessibleTo(TestUsers.Reader("tenant-a")));
    }

    [Theory]
    [InlineData(AppRoles.Reader)]
    [InlineData(AppRoles.Contributor)]
    [InlineData(AppRoles.Admin)]
    public void IsAccessibleTo_OtherTenant_ReturnsFalseWhateverTheRole(string role)
    {
        var document = TestDocuments.Create(TestUsers.Contributor("tenant-a"));
        var outsider = new UserContext("tenant-b", "someone", [role]);

        Assert.False(document.IsAccessibleTo(outsider));
    }

    [Fact]
    public void IsAccessibleTo_RestrictedDocument_RequiresAllowedRoleOrTenantAdmin()
    {
        var document = TestDocuments.Create(TestUsers.Contributor(), allowedRoles: ["Finance.Reader"]);

        Assert.False(document.IsAccessibleTo(TestUsers.Reader()));
        Assert.True(document.IsAccessibleTo(TestUsers.Reader("tenant-a", "Finance.Reader")));
        Assert.True(document.IsAccessibleTo(TestUsers.Admin()));
    }

    [Fact]
    public void IsAccessibleTo_RestrictedDocument_OtherTenantWithMatchingRole_ReturnsFalse()
    {
        var document = TestDocuments.Create(TestUsers.Contributor("tenant-a"), allowedRoles: ["Finance.Reader"]);

        Assert.False(document.IsAccessibleTo(TestUsers.Reader("tenant-b", "Finance.Reader")));
    }

    [Fact]
    public void Create_TakesTenantAndUploaderFromOwner_AndNormalisesRoles()
    {
        var owner = TestUsers.Contributor("tenant-a");

        var document = TestDocuments.Create(owner, "policy.pdf", allowedRoles: [" Finance.Reader ", "Finance.Reader", ""]);

        Assert.Equal("tenant-a", document.TenantId);
        Assert.Equal(owner.UserId, document.UploadedByUserId);
        Assert.Equal("policy.pdf", document.Title);
        Assert.Equal(["Finance.Reader"], document.AllowedRoles);
    }

    [Theory]
    [InlineData("", "user")]
    [InlineData("tenant", " ")]
    public void UserContext_RejectsBlankIdentifiers(string tenantId, string userId)
    {
        Assert.Throws<ArgumentException>(() => new UserContext(tenantId, userId, []));
    }
}
