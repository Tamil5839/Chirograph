using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chirograph.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public string Heading { get; private set; } = "Something went wrong";

    public string Explanation { get; private set; } = "An unexpected error occurred. Please try again.";

    public string? RequestId { get; private set; }

    public void OnGet(int? code) => Describe(code);

    public void OnPost(int? code) => Describe(code);

    private void Describe(int? code)
    {
        (Heading, Explanation) = code switch
        {
            404 => ("Page not found", "That address doesn't exist. If you typed it from a letter, check it for typos."),
            403 => ("Not allowed", "Your account doesn't have access to that page."),
            429 => ("Too many requests", "You've made a lot of requests in a short time. Please wait a few minutes and try again."),
            413 => ("File too large", "That file is larger than the upload limit."),
            _ => (Heading, Explanation),
        };
        if (code is null or >= 500)
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
