using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
public class ProjectsSwitcherTests(AppStackFixture stack) : E2ETestBase(stack)
{
    [Fact]
    public async Task Projects_render_as_cards()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");
        await Expect(Page.Locator(".project-card").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("environment")).First)
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Card_click_opens_overview_and_env_card_navigates_to_secrets()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");
        await Expect(Page.Locator(".project-card").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator(".project-card").First.ClickAsync();
        await Page.WaitForURLAsync("**/projects/**");

        await Expect(Page.GetByText("Recent activity", new() { Exact = true })).ToBeVisibleAsync();

        var trigger = Page.Locator(".dp-combobox__trigger").First;
        await Expect(trigger).ToBeVisibleAsync();
        await Expect(trigger).Not.ToContainTextAsync("Select project");

        var envCard = Page.Locator(".env-card").First;
        await Expect(envCard).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await envCard.ClickAsync();

        await Page.WaitForURLAsync("**/environments/**");
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("· secrets"))).ToBeVisibleAsync();
    }

    [Fact]
    public async Task App_bar_combobox_searches_and_switches_projects()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");
        // Let the page render and the Blazor Server circuit become interactive before clicking,
        // otherwise the trigger click no-ops and the popover never opens.
        await Expect(Page.Locator(".project-card").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var trigger = Page.Locator(".dp-combobox__trigger").First;
        await Expect(trigger).ToBeVisibleAsync();
        var popover = Page.Locator(".dp-combobox-popover.mud-popover-open");
        // Blazor Server: a trigger click can no-op until the circuit is fully interactive. Retry
        // opening, guarded by a visibility check (evaluated after the post-click settle) so we never
        // toggle an already-open popover shut.
        for (var attempt = 0; attempt < 8 && !await popover.IsVisibleAsync(); attempt++)
        {
            await trigger.ClickAsync();
            await Page.WaitForTimeoutAsync(1500);
        }
        await Expect(popover).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(popover.Locator(".dp-command__search")).ToBeFocusedAsync();

        await popover.Locator(".dp-command__search").FillAsync("payments");
        await popover.Locator(".dp-command__item", new() { HasText = "payments-api" }).First.ClickAsync();
        await Page.WaitForURLAsync("**/projects/**");
        await Expect(Page.Locator(".dp-combobox__trigger").First).ToContainTextAsync("payments-api");
    }
}
