#region Copyright
// 
// DotNetNuke® - http://www.dotnetnuke.com
// Copyright (c) 2002-2018
// by DotNetNuke Corporation
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or substantial portions 
// of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED 
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion
#region Usings

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Modules.CoreMessaging.ViewModels;
using DotNetNuke.Security.Roles;
using DotNetNuke.Services.Exceptions;

#endregion

namespace DotNetNuke.Modules.CoreMessaging {

    /// <summary>
    ///   The Settings ModuleSettingsBase is used to manage the 
    ///   settings for the HTML Module
    /// </summary>
    /// <remarks>
    /// </remarks>
    public partial class Settings : ModuleSettingsBase {
        private new CoreMessagingSettings ModuleSettings { get; set; }


        #region Event Handlers

        protected override void OnInit(EventArgs e) {
            base.OnInit(e);
            ModuleSettings = new CoreMessagingSettingsRepository().GetSettings(ModuleConfiguration);
            if (string.IsNullOrWhiteSpace(ModuleSettings.RolePermissions))
                ModuleSettings.RolePermissions = "[]";
        }

        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);
        }

        protected void lstvwRolePermissions_ItemUpdating(object sender, System.Web.UI.WebControls.ListViewUpdateEventArgs e) {
            var editedItem = lstvwRolePermissions.Items[e.ItemIndex];
            var roleIdField = editedItem.FindControl("roleId") as System.Web.UI.WebControls.HiddenField;
            var roleId = int.Parse(roleIdField.Value);

            var allowedRolesField = editedItem.FindControl("cbAllowedRoles") as System.Web.UI.WebControls.CheckBoxList;
            var roleAllowedRoles = allowedRolesField.Items
                .Cast<System.Web.UI.WebControls.ListItem>()
                .Where(li => li.Selected)
                .Select(li => new RoleViewModel {
                    RoleId = int.Parse(li.Value)
                }).ToList();

            var vm = GetViewModel();
            vm.RolePermissions.First(rp => rp.Role.RoleId == roleId).AllowedRoles = roleAllowedRoles;

            SaveSettings(vm);

            lstvwRolePermissions.EditIndex = -1;
            LoadAndBindListView();
        }


        protected void lstvwRolePermissions_ItemDataBound(object sender, System.Web.UI.WebControls.ListViewItemEventArgs e) {
            if (lstvwRolePermissions.EditIndex == e.Item.DataItemIndex) {

                var roleIdField = e.Item.FindControl("roleId") as System.Web.UI.WebControls.HiddenField;
                var roleId = int.Parse(roleIdField.Value);

                var allowedRoles = e.Item.FindControl("cbAllowedRoles") as System.Web.UI.WebControls.CheckBoxList;


                allowedRoles.DataSource = GetRolesViewModel();
                allowedRoles.DataTextField = "RoleName";
                allowedRoles.DataValueField = "RoleId";
                allowedRoles.DataBind();

                var vm = GetViewModel();
                foreach (var role in vm.RolePermissions.First(rp => rp.Role.RoleId == roleId).AllowedRoles) {
                    var ar = allowedRoles.Items.FindByValue(role.RoleId.ToString());
                    if (ar != null)
                        ar.Selected = true;
                }
            }
        }

        protected void lstvwRolePermissions_ItemEditing(object sender, System.Web.UI.WebControls.ListViewEditEventArgs e) {

            lstvwRolePermissions.EditIndex = e.NewEditIndex;
            LoadAndBindListView();

        }

        protected void lstvwRolePermissions_ItemCanceling(object sender, System.Web.UI.WebControls.ListViewCancelEventArgs e) {
            lstvwRolePermissions.EditIndex = -1;
            LoadAndBindListView();
        }

        protected void lstvwRolePermissions_ItemInserting(object sender, System.Web.UI.WebControls.ListViewInsertEventArgs e) {

            var ddNewRole = GetInsertDropDown(e.Item);

            if (string.IsNullOrWhiteSpace(ddNewRole.SelectedValue)) {
                var addNewRoleErrors = GetInsertDropDownErrorDiv(e.Item);
                addNewRoleErrors.Visible = true;
                addNewRoleErrors.InnerText = "Must select a role first.";
            }

            var roleId = int.Parse(ddNewRole.SelectedValue);
            var vm = GetViewModel();
            vm.RolePermissions.Add(new RolePermissionsViewModel {
                Role = new RoleViewModel {
                    RoleId = roleId
                }
            });

            SaveSettings(vm);

            LoadAndBindListView();
        }


        protected void lstvwRolePermissions_ItemDeleting(object sender, System.Web.UI.WebControls.ListViewDeleteEventArgs e) {
            var roleIdField = lstvwRolePermissions.Items[e.ItemIndex].FindControl("roleId") as System.Web.UI.WebControls.HiddenField;
            var roleId = int.Parse(roleIdField.Value);
            var vm = GetViewModel();
            vm.RolePermissions = vm.RolePermissions
                .Where(rp => rp.Role.RoleId != roleId)
                .ToList();

            SaveSettings(vm);

            LoadAndBindListView();
        }

        #endregion

        #region Base Method Implementations

        /// <summary>
        ///   LoadSettings loads the settings from the Database and displays them
        /// </summary>
        /// <remarks>
        /// </remarks>
        public override void LoadSettings() {
            try {
                if (!Page.IsPostBack) {
                    plRolePermissions.Text = "Role based visibility";
                    LoadAndBindListView();
                }
                //Module failed to load
            } catch (Exception exc) {
                Exceptions.ProcessModuleLoadException(this, exc);
            }
        }

        private void LoadAndBindListView() {
            var viewModel = GetViewModel();
            lstvwRolePermissions.DataSource = viewModel.RolePermissions;
            lstvwRolePermissions.DataBind();

            var ddNewRole = GetInsertDropDown(lstvwRolePermissions.InsertItem);
            ddNewRole.DataSource = GetRolesViewModel();
            ddNewRole.DataTextField = nameof(RoleViewModel.RoleName);
            ddNewRole.DataValueField = nameof(RoleViewModel.RoleId);
            ddNewRole.DataBind();
        }

        /// <summary>
        ///   UpdateSettings saves the modified settings to the Database
        /// </summary>
        public override void UpdateSettings() {
            try {


                //SaveSettings(vm);
                //Module failed to load
            } catch (Exception exc) {
                Exceptions.ProcessModuleLoadException(this, exc);
            }
        }

        private void SaveSettings(CoreMessagingSettingsViewModel vm) {
            var settings = GetSettings(vm);

            new CoreMessagingSettingsRepository().SaveSettings(ModuleConfiguration, settings);
            ModuleSettings = settings;
        }

        private CoreMessagingSettings GetSettings(CoreMessagingSettingsViewModel vm) {
            return new CoreMessagingSettings {
                RolePermissions = Newtonsoft.Json.JsonConvert.SerializeObject(
                    vm.RolePermissions.Select(rp => new RolePermissions {
                        RoleId = rp.Role.RoleId,
                        AllowedRoles = rp.AllowedRoles.Select(r => r.RoleId).ToList()
                    }).ToList()
                )
            };
        }

        private CoreMessagingSettingsViewModel GetViewModel() {
            var roleCtrl = RoleController.Instance;
            var roles = roleCtrl.GetRoles(PortalId).ToDictionary(r => r.RoleID, r => r);
            var rolePermissions = Newtonsoft.Json.JsonConvert.DeserializeObject<List<RolePermissions>>(ModuleSettings.RolePermissions);

            RoleViewModel convertRole(int roleId) => new RoleViewModel {
                RoleId = roleId,
                RoleName = roles[roleId].RoleName
            };

            return new CoreMessagingSettingsViewModel {
                RolePermissions = rolePermissions.Select(rp => new RolePermissionsViewModel {
                    Role = convertRole(rp.RoleId),
                    AllowedRoles = rp.AllowedRoles.Select(convertRole).ToList()
                }).ToList()
            };
        }

        private List<RoleViewModel> GetRolesViewModel() {
            var roleCtrl = RoleController.Instance;
            return roleCtrl.GetRoles(PortalId).Select(r => new RoleViewModel {
                RoleId = r.RoleID,
                RoleName = r.RoleName
            }).ToList();
        }

        private System.Web.UI.WebControls.DropDownList GetInsertDropDown(System.Web.UI.WebControls.ListViewItem item) {
            return item.FindControl("ddNewRole") as System.Web.UI.WebControls.DropDownList;
        }

        private System.Web.UI.HtmlControls.HtmlGenericControl GetInsertDropDownErrorDiv(System.Web.UI.WebControls.ListViewItem item) {
            return item.FindControl("addNewRoleErrors") as System.Web.UI.HtmlControls.HtmlGenericControl;
        }

        #endregion
    }
}