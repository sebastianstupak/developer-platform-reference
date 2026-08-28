using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
public class SecretHistoryTests(AppStackFixture stack) : E2ETestBase(stack)
{
    [Fact]
    public async Task Reveal_a_prior_version_and_roll_back()
    {
        await LoginAsync();
        await Page.GotoAsync("/projects");

        // Into the first project, then its first real environment card (the dashed
        // "New environment" placeholder is .env-card--new, so .env-card excludes it).
        // Blazor Server clicks can no-op until the circuit is interactive, so retry each hop
        // (guarded by a content check) and wait on target content rather than a navigation event.
        await Expect(Page.Locator(".project-card").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var envCard = Page.Locator(".env-card").First;
        for (var attempt = 0; attempt < 5 && !await envCard.IsVisibleAsync(); attempt++)
        {
            await Page.Locator(".project-card").First.ClickAsync();
            try
            { await envCard.WaitForAsync(new() { Timeout = 8_000 }); }
            catch (TimeoutException) { }
        }
        await Expect(envCard).ToBeVisibleAsync(new() { Timeout = 30_000 });
        // The retry above established interactivity, so this hop is a single click + content wait.
        await envCard.ClickAsync();
        await Expect(Page.GetByText(new System.Text.RegularExpressions.Regex("· secrets")))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        var name = $"E2E_HIST_{DateTime.UtcNow.Ticks}";

        // Create the secret — this is v1.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add secret" }).ClickAsync();
        await Page.GetByLabel("Name", new() { Exact = true }).FillAsync(name);
        await Page.GetByLabel("Value", new() { Exact = true }).FillAsync("first-value");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Update the secret — appends v2 as the new current version.
        await row.GetByRole(AriaRole.Button, new() { Name = "Edit secret" }).ClickAsync();
        await Page.GetByLabel("Value", new() { Exact = true }).FillAsync("second-value");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Open the version history dialog for that secret. Scope assertions/clicks to the dialog
        // so the row's own "Reveal secret" action (behind the scrim) can't match "Reveal".
        await row.GetByRole(AriaRole.Button, new() { Name = "Version history" }).ClickAsync();
        var history = Page.Locator(".mud-dialog").Filter(new() { HasText = "History" });
        await Expect(history.GetByText("v2", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(history.GetByText("current", new() { Exact = true })).ToBeVisibleAsync();

        // Reveal v1 — versions are listed newest-first, so it's the last exact-"Reveal" button.
        await history.GetByRole(AriaRole.Button, new() { Name = "Reveal", Exact = true }).Last.ClickAsync();
        var reveal = Page.Locator(".mud-dialog").Filter(new() { HasText = "Secret value" });
        await Expect(reveal).ToBeVisibleAsync();
        await reveal.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();

        // Roll back to v1 (only the non-current entry has a "Roll back" button), then confirm.
        await history.GetByRole(AriaRole.Button, new() { Name = "Roll back" }).ClickAsync();
        await Page.Locator(".mud-dialog").Filter(new() { HasText = "Roll back secret" })
            .GetByRole(AriaRole.Button, new() { Name = "Roll back" }).ClickAsync();

        // A new current version (v3) appears, recorded as rolled back from v1.
        await Expect(history.GetByText("v3", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(history.GetByText(new System.Text.RegularExpressions.Regex("rolled back from v1"))).ToBeVisibleAsync();
    }
}
