using System.Reflection;
using DeveloperPlatform.Application.Attributes;
using DeveloperPlatform.Application.Authorization;
using DeveloperPlatform.Application.Context;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Domain.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure.Dispatching;

public sealed class QueryDispatcher(
    IServiceProvider serviceProvider,
    IExecutionContext executionContext,
    IAuthorizationService authorizationService) : IQueryDispatcher
{
    public async Task<TResult> SendAsync<TQuery, TResult>(TQuery query, CancellationToken ct = default)
        where TQuery : IQuery<TResult>
    {
        var requiresPermission = typeof(TQuery).GetCustomAttribute<RequiresPermissionAttribute>();
        if (requiresPermission is not null)
        {
            if (executionContext.PrincipalId is not Guid principalId)
            {
                throw new ForbiddenException("No principal in the execution context.");
            }

            var scope = query is IResourceScoped scoped
                ? scoped.ResourceScope
                : executionContext.EnvironmentId is Guid envId
                    ? Scope.Environment(envId)
                    : executionContext.ProjectId is Guid projId
                        ? Scope.Project(projId)
                        : Scope.Tenant;

            await authorizationService.AuthorizeAsync(principalId, requiresPermission.Permission, scope, ct);
        }

        var handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return await handler.HandleAsync(query, ct);
    }
}
