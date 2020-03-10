using DotNetNuke.Entities.Modules.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetNuke.Modules.CoreMessaging.ViewModels {
    public class CoreMessagingSettingsViewModel {
        public List<RolePermissionsViewModel> RolePermissions { get; set; }
    }

    [DebuggerDisplay("{GetDebugString()}")]
    public class RolePermissionsViewModel {
        public RoleViewModel Role { get; set; }

        public List<RoleViewModel> AllowedRoles { get; set; } = new List<RoleViewModel>();

        private string GetDebugString() {
            return $"{Role} > AllowedRoles: {AllowedRoles.Count}";
        }
    }

    [DebuggerDisplay("{GetDebugString()}")]
    public class RoleViewModel {
        public int RoleId { get; set; }

        public string RoleName { get; set; } = "";

        private string GetDebugString() {
            return $"#{RoleId} {RoleName}";
        }
    }

    public class CoreMessagingSettings {

        [ModuleSetting]
        public string RolePermissions { get; set; }

    }

    public class RolePermissions {
        public int RoleId { get; set; }

        public List<int> AllowedRoles { get; set; }
    }

    public class CoreMessagingSettingsRepository : SettingsRepository<CoreMessagingSettings> {
    }
}
