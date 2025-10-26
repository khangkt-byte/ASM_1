using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ASM_1.Services
{
    public class TableTrackerService : ITableTrackerService
    {
        private readonly ConcurrentDictionary<int, HashSet<string>> _activeGuests = new();
        private readonly ConcurrentDictionary<int, MergeGroupInternal> _mergeGroups = new();
        private readonly ConcurrentDictionary<int, int> _tableToGroup = new();
        private readonly object _mergeLock = new();
        private int _mergeSequence;

        private sealed class MergeGroupInternal
        {
            public int GroupId { get; init; }
            public HashSet<int> TableIds { get; } = new();
            public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
            public string? Label { get; init; }
        }

        public void AddGuest(int tableId, string sessionId)
        {
            var guests = _activeGuests.GetOrAdd(tableId, _ => new HashSet<string>());
            lock (guests)
            {
                guests.Add(sessionId);
            }
        }

        public void RemoveGuest(int tableId, string sessionId)
        {
            if (_activeGuests.TryGetValue(tableId, out var guests))
            {
                lock (guests)
                {
                    guests.Remove(sessionId);
                    if (guests.Count == 0)
                    {
                        _activeGuests.TryRemove(tableId, out _);
                    }
                }
            }
        }

        public int GetGuestCount(int tableId)
        {
            if (_activeGuests.TryGetValue(tableId, out var guests))
            {
                return guests.Count;
            }

            return 0;
        }

        public TableMergeGroupSnapshot MergeTables(IEnumerable<int> tableIds, string? label = null)
        {
            if (tableIds == null) throw new ArgumentNullException(nameof(tableIds));

            var uniqueIds = tableIds.Where(id => id > 0).Distinct().ToList();
            if (uniqueIds.Count < 2)
            {
                throw new ArgumentException("Cần ít nhất 2 bàn để gộp.", nameof(tableIds));
            }

            lock (_mergeLock)
            {
                foreach (var tableId in uniqueIds)
                {
                    if (_tableToGroup.ContainsKey(tableId))
                    {
                        throw new InvalidOperationException($"Bàn {tableId} đã nằm trong nhóm gộp khác.");
                    }
                }

                var groupId = Interlocked.Increment(ref _mergeSequence);
                var group = new MergeGroupInternal
                {
                    GroupId = groupId,
                    Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim()
                };

                foreach (var id in uniqueIds)
                {
                    group.TableIds.Add(id);
                    _tableToGroup[id] = groupId;
                }

                _mergeGroups[groupId] = group;

                return new TableMergeGroupSnapshot(groupId, group.TableIds.ToArray(), group.CreatedAt, group.Label);
            }
        }

        public bool SplitGroup(int groupId)
        {
            lock (_mergeLock)
            {
                if (!_mergeGroups.TryRemove(groupId, out var group))
                {
                    return false;
                }

                foreach (var tableId in group.TableIds)
                {
                    _tableToGroup.TryRemove(tableId, out _);
                }

                return true;
            }
        }

        public bool SplitTable(int tableId)
        {
            lock (_mergeLock)
            {
                if (!_tableToGroup.TryGetValue(tableId, out var groupId))
                {
                    return false;
                }

                if (!_mergeGroups.TryGetValue(groupId, out var group))
                {
                    _tableToGroup.TryRemove(tableId, out _);
                    return false;
                }

                group.TableIds.Remove(tableId);
                _tableToGroup.TryRemove(tableId, out _);

                if (group.TableIds.Count < 2)
                {
                    _mergeGroups.TryRemove(groupId, out _);
                    foreach (var id in group.TableIds)
                    {
                        _tableToGroup.TryRemove(id, out _);
                    }
                }

                return true;
            }
        }

        public IReadOnlyCollection<TableMergeGroupSnapshot> GetMergeGroups()
        {
            lock (_mergeLock)
            {
                return _mergeGroups.Values
                    .Select(g => new TableMergeGroupSnapshot(g.GroupId, g.TableIds.ToArray(), g.CreatedAt, g.Label))
                    .OrderBy(g => g.CreatedAt)
                    .ToList();
            }
        }

        public bool TryGetMergeGroup(int tableId, out TableMergeGroupSnapshot? group)
        {
            group = null;
            if (!_tableToGroup.TryGetValue(tableId, out var groupId))
            {
                return false;
            }

            lock (_mergeLock)
            {
                if (_mergeGroups.TryGetValue(groupId, out var internalGroup))
                {
                    group = new TableMergeGroupSnapshot(internalGroup.GroupId, internalGroup.TableIds.ToArray(), internalGroup.CreatedAt, internalGroup.Label);
                    return true;
                }
            }

            return false;
        }
    }
}
