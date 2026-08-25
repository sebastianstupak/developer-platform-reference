namespace DeveloperPlatform.Domain.Authorization;

// SINGLE SOURCE OF TRUTH for the permission vocabulary.
// The wire token ("resource:action") is derived by PermissionCatalog — never hand-typed.
public enum Permission
{
    [Perm(Resource.Projects, PermissionAction.Read, "View projects")] ProjectsRead,
    [Perm(Resource.Projects, PermissionAction.Write, "Create and edit projects")] ProjectsWrite,
    [Perm(Resource.Secrets, PermissionAction.Read, "Read secret values")] SecretsRead,
    [Perm(Resource.Secrets, PermissionAction.Write, "Set and rotate secrets")] SecretsWrite,
    [Perm(Resource.ApiKeys, PermissionAction.Manage, "Manage API keys")] ApiKeysManage,
    [Perm(Resource.Members, PermissionAction.Manage, "Invite and remove members")] MembersManage,
    [Perm(Resource.ServiceAccounts, PermissionAction.Manage, "Manage service accounts")] ServiceAccountsManage,
    [Perm(Resource.Roles, PermissionAction.Manage, "Assign roles and permissions")] RolesManage,
    [Perm(Resource.Audit, PermissionAction.Read, "View the audit log")] AuditRead,
}
