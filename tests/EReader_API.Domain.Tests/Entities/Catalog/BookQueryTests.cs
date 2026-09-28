using EReader_API.Domain.Entities.Catalog;
using FluentAssertions;

namespace EReader_API.Domain.Tests.Entities.Catalog;

public class BookQueryTests
{
    [Fact]
    public void BookQuery_SameValues_AreEqual()
    {
        var a = new BookQuery(Guid.Empty, BookScope.Public, null, null, 1, 20, null);
        var b = new BookQuery(Guid.Empty, BookScope.Public, null, null, 1, 20, null);

        a.Should().Be(b);
    }

    [Fact]
    public void BookQuery_DifferentScope_AreNotEqual()
    {
        var a = new BookQuery(Guid.Empty, BookScope.Public, null, null, 1, 20, null);
        var b = new BookQuery(Guid.Empty, BookScope.Mine, null, null, 1, 20, null);

        a.Should().NotBe(b);
    }
}
