using Microsoft.Playwright;

namespace DeveloperPlatform.E2ETests.Infrastructure;

[Collection("app-stack")]
public abstract class E2ETestBase(AppStackFixture stack) : IAsyncLifetime
{
    protected AppStackFixture Stack { get; } = stack;
    protected IBrowserContext Context { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Context = await Stack.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Stack.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 1280, Height = 860 },
        });
        Page = await Context.NewPageAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();

    protected async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.WaitForSelectorAsync("#username", new() { Timeout = 30_000 });
        await Page.FillAsync("#username", "dev@example.com");
        await Page.FillAsync("#password", "password");
        await Page.ClickAsync("#kc-login");
        await Page.WaitForURLAsync("**/", new() { Timeout = 30_000 });
    }
}
