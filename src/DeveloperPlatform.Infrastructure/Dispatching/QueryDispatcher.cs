using DeveloperPlatform.Application.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure.Dispatching;

public sealed class QueryDispatcher(IServiceProvider serviceProvider) : IQueryDispatcher
{
    public async Task<TResult> SendAsync<TQuery, TResult>(
        TQuery query, CancellationToken ct = default)
        where TQuery : IQuery<TResult>
    {
        var handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return await handler.HandleAsync(query, ct);
    }
}
