using System.Net;
using Fixtura.Tests.Integration.Hosting;

namespace Fixtura.Tests.Integration.Http;

// The feedback page has two forms that post, each with the antiforgery token Razor Pages checks. The first comes with
// its name filled in and its checkbox checked, as an edit form does.
public class FormTests(PlainFixture api) : IClassFixture<PlainFixture>
{
    [Fact]
    public async Task SubmitFormAsync_ShouldPostTheFormWithItsToken_WhenTheValuesFitOneForm()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.SubmitFormAsync("/Feedback", new { Name = "ann", Message = "Loved it" });

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Thanks, ann: Loved it (subscribed)</p>", page);
    }

    [Fact]
    public async Task SubmitFormAsync_ShouldSendTheFormsOwnValues_WhenGivenOnlySomeFields()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.SubmitFormAsync("/Feedback", new { Message = "Hi" });

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Thanks, guest: Hi (subscribed)</p>", page);
    }

    // The checked checkbox's own "true" mustn't win over the value the test gives.
    [Fact]
    public async Task SubmitFormAsync_ShouldReplaceTheFormsValue_WhenGivenThatField()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.SubmitFormAsync("/Feedback", new { Message = "Hi", Subscribe = false });

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Thanks, guest: Hi</p>", page);
    }

    // Only the second form has a Reason field, and it posts to its own handler.
    [Fact]
    public async Task SubmitFormAsync_ShouldPostToTheFormsOwnAction_WhenOnlyThatFormHasTheFields()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.SubmitFormAsync("/Feedback", new { Reason = "Spam" });

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Cleared: Spam", page);
    }

    [Fact]
    public async Task SubmitFormAsync_ShouldListThePagesForms_WhenSeveralFitTheValues()
    {
        HttpClient client = await api.CreateClientAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitFormAsync("/Feedback", new { }));

        Assert.Contains("has 2 forms with a field for each value: (this page) with Name, Message, Subscribe", error.Message);
        Assert.Contains("/Feedback?handler=Clear with Reason, __RequestVerificationToken", error.Message);
    }

    [Fact]
    public async Task SubmitFormAsync_ShouldListThePagesForms_WhenNoFormHasAFieldForAValue()
    {
        HttpClient client = await api.CreateClientAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SubmitFormAsync("/Feedback", new { Nmae = "ann" }));

        Assert.Contains("has 0 forms with a field for each value", error.Message);
    }

    [Fact]
    public async Task SubmitFormAsync_ShouldSayThePageHasNoForm_WhenItHasNone()
    {
        HttpClient client = await api.CreateClientAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitFormAsync("/ping", new { }));

        Assert.Equal("The page at /ping answered 200 with no form that posts.", error.Message);
    }

    [Fact]
    public async Task SubmitFormAsync_ShouldPostTheEntries_WhenGivenADictionary()
    {
        HttpClient client = await api.CreateClientAsync();

        HttpResponseMessage response = await client.SubmitFormAsync("/Feedback",
            new Dictionary<string, object?> { ["Name"] = "sam", ["Message"] = 42 });

        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Thanks, sam: 42 (subscribed)</p>", page);
    }
}
