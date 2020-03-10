<%@ Control Inherits="DotNetNuke.Modules.CoreMessaging.Settings" CodeBehind="Settings.ascx.cs" Language="C#" AutoEventWireup="false" %>
<%@ Import Namespace="DotNetNuke.Modules.CoreMessaging.ViewModels" %>
<%@ Register TagPrefix="dnn" TagName="Label" Src="~/controls/LabelControl.ascx" %>
<%@ Register TagPrefix="dnn" Namespace="DotNetNuke.Web.UI.WebControls" Assembly="DotNetNuke.Web" %>
<%@ Register TagPrefix="dnncl" Namespace="DotNetNuke.Web.Client.ClientResourceManagement" Assembly="DotNetNuke.Web.Client" %>

<dnncl:DnnCssInclude ID="customJS" runat="server" FilePath="DesktopModules/CoreMessaging/edit.css" AddTag="false" />

<div class="dnnForm dnnCoreMessagingSettings dnnClear">
    <div class="dnnFormItem">
        <%--<dnn:label id="plRolePermissions" controlname="lstvwRolePermissions" runat="server" />--%>
        <asp:ListView ID="lstvwRolePermissions" runat="server"
            InsertItemPosition="LastItem"
            OnItemInserting="lstvwRolePermissions_ItemInserting"
            OnItemEditing="lstvwRolePermissions_ItemEditing"
            OnItemDeleting="lstvwRolePermissions_ItemDeleting">

            <LayoutTemplate>
                <ul runat="server" id="lstRolePerms">
                    <li runat="server" id="itemPlaceholder" />
                </ul>
            </LayoutTemplate>

            <ItemTemplate>
                <li runat="server">
                    <asp:HiddenField ID="roleId" runat="server" Value='<%# Eval("Role.RoleId" ) %>' />
                    <span><%# Eval("Role.RoleName" ) %></span>
                    <a href="#"><%# Eval("AllowedRoles.Count") %> Allowed roles</a>
                    <asp:LinkButton ID="btnDeleteRole" runat="server" CommandName="Delete" Text="Delete" CssClass="btn-delete-role dnnSecondaryAction" />
                </li>
            </ItemTemplate>

            <InsertItemTemplate>
                <asp:DropDownList ID="ddNewRole" AppendDataBoundItems="false" runat="server"></asp:DropDownList>
                <asp:LinkButton ID="btnAddNewRole" runat="server" CommandName="Insert" Text="Add Role" CssClass="dnnSecondaryAction" />
                <div id="addNewRoleErrors" runat="server" visible="false"></div>
            </InsertItemTemplate>

            <EditItemTemplate>
                <div><%# Eval("Role.RoleName" ) %></div>
                <asp:CheckBoxList ID="cbAllowedRoles" runat="server"></asp:CheckBoxList>
            </EditItemTemplate>
        </asp:ListView>

    </div>
    <%--<div class="dnnFormItem">
		<dnn:label id="plReplaceTokens" controlname="chkReplaceTokens" runat="server" />
		<asp:CheckBox ID="chkReplaceTokens" runat="server" />
	</div>
	<div class="dnnFormItem">
		<dnn:label id="plDecorate" controlname="cbDecorate" runat="server" />
		<asp:CheckBox ID="cbDecorate" runat="server" />
	</div>
	<div class="dnnFormItem">
        <dnn:label id="plSearchDescLength" runat="server" controlname="txtSearchDescLength" />
        <asp:TextBox ID="txtSearchDescLength" runat="server" />
        <asp:RegularExpressionValidator ID="RegularExpressionValidator1" runat="server" ControlToValidate="txtSearchDescLength"
            Display="Dynamic" CssClass="dnnFormMessage dnnFormError" ValidationExpression="^\d+$" resourcekey="valSearchDescLength.ErrorMessage" />
    </div>
	<div class="dnnFormItem">
		<dnn:label id="plWorkflow" controlname="cboWorkflow" runat="server" suffix=":" />		
        <dnn:DnnComboBox ID="cboWorkflow" runat="server" DataTextField="WorkflowName" DataValueField="WorkflowID" AutoPostBack="True" />
	</div>
	<div class="dnnFormMessage dnnFormInfo">
		<asp:Label ID="lblDescription" runat="server" />
	</div>
	<div class="dnnFormItem" id="divApplyTo" runat="server">
		<dnn:label id="plApplyTo" controlname="rblApplyTo" runat="server" />
		<asp:RadioButtonList ID="rblApplyTo" runat="server" RepeatDirection="Horizontal" CssClass="dnnFormRadioButtons">
			<asp:ListItem Value="Module" ResourceKey="Module" />
			<asp:ListItem Value="Page" ResourceKey="Page" />
			<asp:ListItem Value="Site" ResourceKey="Site" />
		</asp:RadioButtonList>
		<asp:CheckBox ID="chkReplace" runat="server" resourcekey="chkReplace" CssClass="inline" />
	</div>--%>
</div>

<script type="text/javascript">
    $(document).ready(function () {
        $('#btn-add-role-permission').click(function () {

        });
    });
</script>
