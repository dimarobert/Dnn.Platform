<%@ Control Inherits="DotNetNuke.Modules.CoreMessaging.Settings" CodeBehind="Settings.ascx.cs" Language="C#" AutoEventWireup="false" %>
<%@ Import Namespace="DotNetNuke.Modules.CoreMessaging.ViewModels" %>
<%@ Register TagPrefix="dnn" TagName="Label" Src="~/controls/LabelControl.ascx" %>
<%@ Register TagPrefix="dnn" Namespace="DotNetNuke.Web.UI.WebControls" Assembly="DotNetNuke.Web" %>
<%@ Register TagPrefix="dnncl" Namespace="DotNetNuke.Web.Client.ClientResourceManagement" Assembly="DotNetNuke.Web.Client" %>

<dnncl:DnnCssInclude ID="customJS" runat="server" FilePath="DesktopModules/CoreMessaging/edit.css" AddTag="false" />

<div class="dnnForm dnnCoreMessagingSettings dnnClear">
    <div class="dnnFormItem">
        <asp:Label id="plRolePermissions" runat="server" />
        <hr />
        <asp:ListView ID="lstvwRolePermissions" runat="server"
            InsertItemPosition="LastItem"
            OnItemInserting="lstvwRolePermissions_ItemInserting"
            OnItemEditing="lstvwRolePermissions_ItemEditing"
            OnItemDeleting="lstvwRolePermissions_ItemDeleting"
            OnItemUpdating="lstvwRolePermissions_ItemUpdating"
            OnItemDataBound="lstvwRolePermissions_ItemDataBound"
            OnItemCanceling="lstvwRolePermissions_ItemCanceling">

            <LayoutTemplate>
                <ul runat="server" id="lstRolePerms" class="role-list">
                    <li runat="server" id="itemPlaceholder" />
                </ul>
            </LayoutTemplate>

            <ItemTemplate>
                <li runat="server">
                    <asp:HiddenField ID="roleId" runat="server" Value='<%# Eval("Role.RoleId" ) %>' />
                    <span><%# Eval("Role.RoleName" ) %></span>
                    <asp:LinkButton ID="btnEditRole" runat="server" CommandName="Edit" Text='<%# Eval("AllowedRoles.Count") + " Allowed roles" %>' />
                    <asp:LinkButton ID="btnDeleteRole" runat="server" CommandName="Delete" Text="Delete" CssClass="btn-delete-role dnnSecondaryAction" />
                </li>
                <hr />
            </ItemTemplate>

            <EditItemTemplate>
                <asp:HiddenField ID="roleId" runat="server" Value='<%# Eval("Role.RoleId" ) %>' />

                <div>Select visible permissions for <%# Eval("Role.RoleName" ) %></div>
                <asp:CheckBoxList ID="cbAllowedRoles" runat="server"></asp:CheckBoxList>
                <asp:LinkButton ID="LinkButton1" runat="server" CommandName="Update" Text="Update" CssClass="dnnPrimaryAction" />
                <asp:LinkButton ID="btnCancelRole" runat="server" CommandName="Cancel" Text="Cancel" CssClass="dnnSecondaryAction" />
                <hr />
            </EditItemTemplate>

            <InsertItemTemplate>
                <div>
                    <asp:DropDownList ID="ddNewRole" AppendDataBoundItems="false" runat="server"></asp:DropDownList>
                    <asp:LinkButton ID="btnAddNewRole" runat="server" CommandName="Insert" Text="Add Role" CssClass="dnnSecondaryAction" />
                    <div id="addNewRoleErrors" runat="server" visible="false"></div>
                </div>
            </InsertItemTemplate>
        </asp:ListView>
        <hr />

    </div>
</div>
