namespace DeveloperPlatform.Application.Commands;

public interface ICommandDispatcher
{
    Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand<TResult>;
}
