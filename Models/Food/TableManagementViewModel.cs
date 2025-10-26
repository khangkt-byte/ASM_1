using System;
using System.Collections.Generic;

namespace ASM_1.Models.Food
{
    public class TableManagementViewModel
    {
        public List<Table> Tables { get; set; } = new();
        public List<TableMergeGroupViewModel> ActiveMerges { get; set; } = new();
    }

    public class TableMergeGroupViewModel
    {
        public int GroupId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<Table> Tables { get; set; } = new();
    }
}
