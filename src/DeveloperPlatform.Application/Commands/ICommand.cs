namespace DeveloperPlatform.Application.Commands;

public interface ICommand<TResult> { }

public interface ICommand : ICommand<Unit> { }

public readonly struct Unit
{
    public static Unit Value => default;
}
