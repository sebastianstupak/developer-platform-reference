using DeveloperPlatform.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace DeveloperPlatform.E2ETests;

[Trait("Category", "E2E")]
public class AuditFiltersTests(AppStackFixture stack) : E2ETestBase(stack)
{
    private const string ActionCell = "tbody tr td:nth-child(3)";
    private const string StatusCell = "tbody tr td:nth-child(4)";

    private async Task GotoAuditAsync()
    {
        await LoginAsync();
        await Page.GotoAsync("/audit");
        await Expect(Page.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Action_multiselect_narrows_grid_to_chosen_command_types()
    {
        await GotoAuditAsync();
        // The seeder creates/sets secrets (SetSecretCommand) and creates projects
        // (CreateProjectCommand) but never reveals a secret, so RevealSecretCommand isn't
        // guaranteed to exist — use two command types the seed reliably produces instead.
        string[] wanted = ["SetSecretCommand", "CreateProjectCommand"];
        await Page.GetByLabel("Action", new() { Exact = true }).ClickAsync();
        foreach (var w in wanted)
        {
            await Page.Locator(".mud-list-item", new() { HasText = w }).ClickAsync();
        }

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Page.Locator(ActionCell).First).ToBeVisibleAsync();
        // Playwright .NET has no expect.poll() equivalent — poll in-page via WaitForFunctionAsync
        // until every visible Action cell is one of the selected command types.
        await Page.WaitForFunctionAsync(
            """
            ([selector, wanted]) => {
                const cells = Array.from(document.querySelectorAll(selector)).map(td => td.textContent.trim());
                return cells.length > 0 && cells.every(c => wanted.includes(c));
            }
            """,
            new object[] { ActionCell, wanted });
    }

    [Fact]
    public async Task Status_multiselect_keeps_successes_and_failures()
    {
        await GotoAuditAsync();
        await Page.GetByLabel("Status", new() { Exact = true }).ClickAsync();
        await Page.Locator(".mud-list-item", new() { HasText = "Success" }).ClickAsync();
        await Page.Locator(".mud-list-item", new() { HasText = "Failed" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Page.WaitForFunctionAsync(
            """
            selector => {
                const cells = Array.from(document.querySelectorAll(selector)).map(td => td.textContent.trim());
                return cells.length > 0 && cells.every(c => c === 'Success' || c === 'Failed');
            }
            """,
            StatusCell);
    }

    [Fact]
    public async Task Actor_search_selects_an_actor_as_a_removable_chip()
    {
        await GotoAuditAsync();
        var actor = Page.GetByLabel("Actor", new() { Exact = true });
        await actor.ClickAsync();
        await actor.FillAsync("dev"); // matches the seeded dev@example.com actor

        await Page.Locator(".mud-list-item").First.ClickAsync();

        var filterChips = Page.Locator(".pa-4 .mud-chip");
        await Expect(filterChips).ToHaveCountAsync(1);
        await Expect(Page.Locator("tbody tr").First).ToBeVisibleAsync();

        await filterChips.First.Locator("button").First.ClickAsync();
        await Expect(filterChips).ToHaveCountAsync(0);
    }
}
