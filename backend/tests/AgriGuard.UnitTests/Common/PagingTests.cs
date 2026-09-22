using AgriGuard.Application.Common.Models;

namespace AgriGuard.UnitTests.Common;

public sealed class PagingTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Page_number_is_clamped_to_at_least_one(int requested, int expected)
    {
        Assert.Equal(expected, new PageRequest { Page = requested }.NormalisedPage);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(100, 100)]
    [InlineData(5000, 100)]
    public void Page_size_is_clamped_to_the_maximum(int requested, int expected)
    {
        // The cap is what stops one request scanning the whole table.
        Assert.Equal(expected, new PageRequest { PageSize = requested }.NormalisedPageSize);
    }

    [Fact]
    public void Skip_follows_from_the_normalised_values()
    {
        Assert.Equal(0, new PageRequest { Page = 1, PageSize = 20 }.Skip);
        Assert.Equal(40, new PageRequest { Page = 3, PageSize = 20 }.Skip);
        // Page 0 is treated as page 1, so nothing is skipped.
        Assert.Equal(0, new PageRequest { Page = 0, PageSize = 20 }.Skip);
    }

    [Fact]
    public void Total_pages_rounds_up_so_a_partial_page_still_counts()
    {
        var result = new PagedResult<string>(["a", "b"], Page: 3, PageSize: 20, TotalCount: 41);

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void An_empty_result_has_no_pages_and_no_neighbours()
    {
        var empty = PagedResult<string>.Empty(page: 1, pageSize: 20);

        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalPages);
        Assert.False(empty.HasNextPage);
        Assert.False(empty.HasPreviousPage);
    }
}
