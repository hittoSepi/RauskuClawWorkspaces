using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RauskuClaw.Models
{
    /// <summary>
    /// Template for creating workspaces with predefined configurations.
    /// </summary>
    public class WorkspaceTemplate : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _description = "";
        private string _category = "";
        private int _memoryMb = 4096;
        private int _cpuCores = 4;
        private string _username = "rausku";
        private string _hostname = "rausku-vm";
        private List<TemplatePortMapping> _portMappings = new();
        private List<string> _enabledServices = new();
        private string _icon = "";
        private bool _isDefault = false;
        private string _source = TemplateSources.BuiltIn;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public int MemoryMb
        {
            get => _memoryMb;
            set { _memoryMb = value; OnPropertyChanged(); }
        }

        public int CpuCores
        {
            get => _cpuCores;
            set { _cpuCores = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string Hostname
        {
            get => _hostname;
            set { _hostname = value; OnPropertyChanged(); }
        }

        public List<TemplatePortMapping> PortMappings
        {
            get => _portMappings;
            set { _portMappings = value; OnPropertyChanged(); }
        }

        public List<string> EnabledServices
        {
            get => _enabledServices;
            set { _enabledServices = value; OnPropertyChanged(); }
        }

        public string Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set { _isDefault = value; OnPropertyChanged(); }
        }

        public string Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Create a Workspace from this template.
        /// </summary>
        public Workspace CreateWorkspace()
        {
            var workspace = new Workspace
            {
                Name = Name,
                Description = Description,
                Username = Username,
                Hostname = Hostname,
                MemoryMb = MemoryMb,
                CpuCores = CpuCores
            };

            // Apply port mappings
            if (PortMappings.Count > 0)
            {
                workspace.Ports = new PortAllocation
                {
                    Ssh = PortMappings.Find(p => p.Name == "SSH")?.Port ?? 2222,
                    Api = PortMappings.Find(p => p.Name == "API")?.Port ?? 3011,
                    UiV2 = PortMappings.Find(p => p.Name == "UIv2")?.Port ?? 3013,
                    UiV1 = PortMappings.Find(p => p.Name == "UIv1")?.Port ?? 3012,
                    Qmp = PortMappings.Find(p => p.Name == "QMP")?.Port ?? 4444,
                    Serial = PortMappings.Find(p => p.Name == "Serial")?.Port ?? 5555
                };
            }

            return workspace;
        }
    }

    /// <summary>
    /// Port mapping for template.
    /// </summary>
    public class TemplatePortMapping
    {
        public string Name { get; set; } = "";
        public int Port { get; set; }
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Template categories for organization.
    /// </summary>
    public static class TemplateCategories
    {
        public const string Default = "Default";
        public const string Minimal = "Minimal";
        public const string FullAI = "Full AI Stack";
        public const string Development = "Development";
        public const string Production = "Production";
        public const string Custom = "Custom";
    }

    public static class TemplateSources
    {
        public const string BuiltIn = "Built-in";
        public const string Custom = "Custom";
    }
}
