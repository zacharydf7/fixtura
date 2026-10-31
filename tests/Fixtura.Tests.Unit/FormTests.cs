using System.Net;

namespace Fixtura.Tests.Unit;

public class FormTests
{
    [Fact]
    public void ThatPost_ShouldReadWhatABrowserWouldSend_WhenGivenAFilledForm()
    {
        const string html = """
            <form method="post">
              <input name="Title" value="Dune &amp; more" />
              <input type='checkbox' name='Gift' value='yes' />
              <input type=checkbox name=Signed checked />
              <input type="radio" name="Cover" value="hard" checked>
              <input type="radio" name="Cover" value="soft">
              <textarea name="Notes">
            First line</textarea>
              <select name="Format"><option value="ebook">E-book</option><option selected>Print</option></select>
              <select name="Shelf"><option>Top</option><option>Bottom</option></select>
              <input type="submit" name="Save" value="Save" />
              <input name="Locked" value="x" disabled />
              <input type="checkbox" name="Published" value="true" checked="checked" />
              <select name="Genre"><option value="1">Fiction</option><option value="2" selected="selected">Poetry</option></select>
              <select name="Tags" multiple="multiple"><option selected="selected">new</option><option>old</option></select>
              <input name="Archived" value="y" disabled="disabled" />
            </form>
            """;

        HtmlForm form = Assert.Single(HtmlForm.ThatPost(html));

        Assert.Equal(
        [
            new("Title", "Dune & more"), new("Signed", "on"), new("Cover", "hard"), new("Notes", "First line"),
            new("Format", "Print"), new("Shelf", "Top"), new("Published", "true"), new("Genre", "2"), new("Tags", "new")
        ], form.Values);
        Assert.Equal(["Title", "Gift", "Signed", "Cover", "Notes", "Format", "Shelf", "Save", "Published", "Genre", "Tags"],
            form.FieldNames);
    }

    [Fact]
    public void ThatPost_ShouldSkipFormsThatDoNotPost_WhenGivenSeveral()
    {
        const string html = """
            <form action="/search"><input name="q" /></form>
            <form method="POST" action="/books?handler=Delete&amp;id=3"><input type="hidden" name="Id" value="3" /></form>
            """;

        HtmlForm form = Assert.Single(HtmlForm.ThatPost(html));

        Assert.Equal("/books?handler=Delete&id=3", form.Action);
    }

    [Fact]
    public void ValuesOf_ShouldRepeatAListAndWriteDatesInIso_WhenGivenAnObject()
    {
        var values = new { Tags = new[] { "new", "sale" }, Published = new DateOnly(2026, 1, 15), Price = 9.5m };

        List<KeyValuePair<string, string>> fields = HttpClientForms.ValuesOf(values);

        Assert.Equal([new("Tags", "new"), new("Tags", "sale"), new("Published", "2026-01-15"), new("Price", "9.5")], fields);
    }

    // A misspelled field mustn't post silently, even when the page has one form.
    [Fact]
    public async Task SubmitFormAsync_ShouldThrow_WhenThePagesOnlyFormHasNoFieldForAValue()
    {
        using var client = new HttpClient(new PageHandler("""<form method="post"><input name="Title" /></form>"""))
        {
            BaseAddress = new Uri("http://shop.test")
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SubmitFormAsync("/books/new", new { Titel = "Dune" }));

        Assert.Contains("has 0 forms with a field for each value: (this page) with Title", error.Message);
    }

    private sealed class PageHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html), RequestMessage = request });
    }
}
