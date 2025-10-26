namespace ASM_1.Services
{
    public interface ITableTrackerService
    {
        void AddGuest(int tableId, string sessionId);
        void RemoveGuest(int tableId, string sessionId);
        int GetGuestCount(int tableId);
        TableMergeGroupSnapshot MergeTables(IEnumerable<int> tableIds, string? label = null);
        bool SplitGroup(int groupId);
        bool SplitTable(int tableId);
        IReadOnlyCollection<TableMergeGroupSnapshot> GetMergeGroups();
        bool TryGetMergeGroup(int tableId, out TableMergeGroupSnapshot? group);
    }
}
