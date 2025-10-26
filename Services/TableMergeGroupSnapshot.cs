using System;
using System.Collections.Generic;

namespace ASM_1.Services
{
    public record TableMergeGroupSnapshot(int GroupId, IReadOnlyCollection<int> TableIds, DateTime CreatedAt, string? Label);
}
