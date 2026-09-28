using EReader_API.Domain.Entities.Catalog;
using FluentAssertions;

namespace EReader_API.Domain.Tests.Entities.Catalog;

public class BookSourceTests
{
    // O CHECK constraint em BookConfiguration e o índice em Book(Source) gravam esses valores
    // como int no banco (via migration AddBookCatalog) — mudar a ordem do enum sem uma migration
    // corrompe silenciosamente os dados já gravados.
    [Fact]
    public void BookSource_UnderlyingValues_AreStable()
    {
        ((int)BookSource.PublicDomain).Should().Be(0);
        ((int)BookSource.UserUpload).Should().Be(1);
    }
}
