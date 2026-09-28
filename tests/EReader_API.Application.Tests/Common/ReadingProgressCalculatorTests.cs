using EReader_API.Application.Common;
using FluentAssertions;

namespace EReader_API.Application.Tests.Common;

public class ReadingProgressCalculatorTests
{
    [Theory]
    [InlineData(5, null, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(1, 3, 33.33)]
    [InlineData(10, 10, 100)]
    [InlineData(0, 200, 0)]
    public void PercentComplete_VariousInputs_ReturnsExpected(
        int currentPage, int? totalPages, decimal expected)
    {
        ReadingProgressCalculator.PercentComplete(currentPage, totalPages).Should().Be(expected);
    }
}
