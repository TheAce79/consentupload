using System.ComponentModel;
using ConsentSyncCore.Collections;
using Xunit;

namespace Orchestrator.Tests;

public sealed class SortableBindingListTests
{
    [Fact]
    public void ApplySort_SortsFullNameAscendingAndDescending()
    {
        var rows = new SortableBindingList<TestRow>([
            new("Zulu"), new("albert"), new("Richard")
        ]);
        IBindingList bindingList = rows;
        PropertyDescriptor fullName = TypeDescriptor.GetProperties(typeof(TestRow))[nameof(TestRow.FullName)]!;

        bindingList.ApplySort(fullName, ListSortDirection.Ascending);
        Assert.Equal(["albert", "Richard", "Zulu"], rows.Select(row => row.FullName));
        Assert.True(bindingList.IsSorted);
        Assert.Equal(ListSortDirection.Ascending, bindingList.SortDirection);

        bindingList.ApplySort(fullName, ListSortDirection.Descending);
        Assert.Equal(["Zulu", "Richard", "albert"], rows.Select(row => row.FullName));
        Assert.Equal(ListSortDirection.Descending, bindingList.SortDirection);
    }

    private sealed record TestRow(string FullName);
}
