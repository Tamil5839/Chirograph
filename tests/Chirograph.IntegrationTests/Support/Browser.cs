using System.Net;
using System.Net.Http.Headers;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Chirograph.IntegrationTests.Support;

/// <summary>A parsed HTML response.</summary>
public sealed class HtmlPage(Uri url, HttpResponseMessage response, IDocument document)
{
    public Uri Url { get; } = url;

    public HttpStatusCode StatusCode => Response.StatusCode;

    public HttpResponseMessage Response { get; } = response;

    public IDocument Document { get; } = document;

    public string Text => Document.Body?.TextContent ?? string.Empty;

    public string? TextOf(string selector) => Document.QuerySelector(selector)?.TextContent.Trim();

    public string? AttributeOf(string selector, string attribute) => Document.QuerySelector(selector)?.GetAttribute(attribute);

    public static async Task<HtmlPage> LoadAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var url = response.RequestMessage!.RequestUri!;
        var document = await BrowsingContext.New(Configuration.Default).OpenAsync(request => request.Content(html).Address(url));
        return new HtmlPage(url, response, document);
    }
}

/// <summary>Fills in and submits real forms, including their antiforgery tokens, like a browser would.</summary>
public sealed class Browser(HttpClient client) : IDisposable
{
    public HttpClient Client { get; } = client;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async Task<HtmlPage> GetAsync(string url) => await HtmlPage.LoadAsync(await Client.GetAsync(url, Token));

    public Task<HttpResponseMessage> GetRawAsync(string url) => Client.GetAsync(url, Token);

    public async Task<HtmlPage> SubmitAsync(
        HtmlPage page,
        string formSelector,
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyDictionary<string, (string FileName, byte[] Content)>? files = null) =>
        await HtmlPage.LoadAsync(await SubmitRawAsync(page, formSelector, values, files));

    public async Task<HttpResponseMessage> SubmitRawAsync(
        HtmlPage page,
        string formSelector,
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyDictionary<string, (string FileName, byte[] Content)>? files = null)
    {
        var form = page.Document.QuerySelector<IHtmlFormElement>(formSelector)
            ?? throw new InvalidOperationException($"No form '{formSelector}' on {page.Url}.");

        var fields = new List<KeyValuePair<string, string>>();
        foreach (var element in form.Elements)
        {
            switch (element)
            {
                case IHtmlInputElement input when input.Type is "submit" or "button" or "image" or "file" || string.IsNullOrEmpty(input.Name):
                    break;
                case IHtmlInputElement input when input.Type is "checkbox" or "radio":
                    if (input.IsChecked)
                        fields.Add(new(input.Name!, input.Value));
                    break;
                case IHtmlInputElement input:
                    fields.Add(new(input.Name!, input.Value));
                    break;
                case IHtmlSelectElement select when !string.IsNullOrEmpty(select.Name):
                    fields.Add(new(select.Name, select.Value ?? string.Empty));
                    break;
                case IHtmlTextAreaElement textArea when !string.IsNullOrEmpty(textArea.Name):
                    fields.Add(new(textArea.Name, textArea.Value));
                    break;
            }
        }
        foreach (var (name, value) in values ?? new Dictionary<string, string>())
        {
            fields.RemoveAll(field => field.Key == name);
            fields.Add(new(name, value));
        }

        HttpContent content;
        if (form.Enctype == "multipart/form-data")
        {
            var multipart = new MultipartFormDataContent();
            foreach (var (name, value) in fields)
                multipart.Add(new StringContent(value), name);
            foreach (var (name, file) in files ?? new Dictionary<string, (string, byte[])>())
            {
                var part = new ByteArrayContent(file.Content);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                multipart.Add(part, name, file.FileName);
            }
            content = multipart;
        }
        else
        {
            content = new FormUrlEncodedContent(fields);
        }
        return await Client.PostAsync(form.Action, content, Token);
    }

    public void Dispose() => Client.Dispose();
}
