using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SampleApp.Pages;

// Two forms that post, each with the antiforgery token Razor Pages checks.
public sealed class FeedbackModel : PageModel
{
    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public string Message { get; set; } = "";

    [BindProperty]
    public bool Subscribe { get; set; }

    public string? Thanks { get; private set; }

    // Prefilled, as an edit form is, so a test that changes only the message still sends the name and the subscription.
    public void OnGet()
    {
        Name = "guest";
        Subscribe = true;
    }

    public void OnPost() => Thanks = $"Thanks, {Name}: {Message}{(Subscribe ? " (subscribed)" : "")}";

    public void OnPostClear(string reason) => Thanks = $"Cleared: {reason}";
}
