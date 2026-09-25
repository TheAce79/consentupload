using System.ComponentModel;
using System.Globalization;

namespace ConsentSyncCore.Collections;

public sealed class SortableBindingList<T> : BindingList<T>
{
    private bool _isSorted;
    private PropertyDescriptor? _sortProperty;
    private ListSortDirection _sortDirection;

    public SortableBindingList(IEnumerable<T> items) : base(items.ToList())
    {
    }

    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _isSorted;
    protected override PropertyDescriptor? SortPropertyCore => _sortProperty;
    protected override ListSortDirection SortDirectionCore => _sortDirection;

    protected override void ApplySortCore(PropertyDescriptor property, ListSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(property);

        List<T> sorted = direction == ListSortDirection.Ascending
            ? Items.OrderBy(item => property.GetValue(item), PropertyValueComparer.Instance).ToList()
            : Items.OrderByDescending(item => property.GetValue(item), PropertyValueComparer.Instance).ToList();

        bool raiseEvents = RaiseListChangedEvents;
        RaiseListChangedEvents = false;
        try
        {
            for (int index = 0; index < sorted.Count; index++) Items[index] = sorted[index];
            _sortProperty = property;
            _sortDirection = direction;
            _isSorted = true;
        }
        finally
        {
            RaiseListChangedEvents = raiseEvents;
        }

        if (raiseEvents) ResetBindings();
    }

    protected override void RemoveSortCore()
    {
        _isSorted = false;
        _sortProperty = null;
    }

    private sealed class PropertyValueComparer : IComparer<object?>
    {
        public static readonly PropertyValueComparer Instance = new();

        public int Compare(object? left, object? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            if (left is string || right is string)
                return string.Compare(Convert.ToString(left, CultureInfo.CurrentCulture), Convert.ToString(right, CultureInfo.CurrentCulture), StringComparison.CurrentCultureIgnoreCase);
            return left is IComparable comparable
                ? comparable.CompareTo(right)
                : string.Compare(left.ToString(), right.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
