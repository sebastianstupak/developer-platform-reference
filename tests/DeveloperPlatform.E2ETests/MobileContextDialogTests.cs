using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
[Collection("app-stack")]
public class MobileContextDialogTests(AppStackFixture stack) : IAsyncLifetime
{
    private IBrowserContext _context = default!;
    private IPage _page = default!;

    public async Task InitializeAsync()
    {
        _context = await stack.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = stack.WebBaseUrl,
            ViewportSize = new ViewportSize { Width = 390, Height = 850 },
        });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Acronym_button_opens_dialog_to_switch_project()
    {
        await _page.GotoAsync("/login");
        await _page.WaitForSelectorAsync("#username", new() { Timeout = 30_000 });
        await _page.FillAsync("#username", "dev@example.com");
        await _page.FillAsync("#password", "password");
        await _page.ClickAsync("#kc-login");
        await _page.WaitForURLAsync("**/", new() { Timeout = 30_000 });
        await _page.GotoAsync("/projects");

        await _page.Locator(".dp-ctxbtn").ClickAsync();
        await Expect(_page.Locator(".dp-ctxdlg")).ToBeVisibleAsync();
        await _page.Locator(".dp-ctxdlg .dp-command__item", new() { HasText = "payments-api" }).First.ClickAsync();
        await _page.WaitForURLAsync("**/projects/**");
        await Expect(_page.Locator(".dp-ctxbtn")).ToContainTextAsync("payments-api");
    }
}
