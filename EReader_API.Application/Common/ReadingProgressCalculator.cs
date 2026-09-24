namespace EReader_API.Application.Common;

public static class ReadingProgressCalculator
{
    public static decimal PercentComplete(int currentPage, int? totalPages) =>
        totalPages is > 0 ? Math.Round(100m * currentPage / totalPages.Value, 2) : 0m;
}
