using System.Collections;
using System.Globalization;
using Xunit;

namespace Fixtura;

/// <summary>Submits the app's HTML forms as a browser does, antiforgery token included.</summary>
public static class HttpClientForms
{
    /// <summary>
    /// Opens the page at <paramref name="path"/> and submits the form that has a field for each of
    /// <paramref name="values"/>, such as <c>new { Title = "Dune", Copies = 2 }</c>, a dictionary, or name and value pairs.
    /// The form's other fields keep what a browser would send, such as a prefilled input's value, a checked checkbox or
    /// the antiforgery token, and it posts to its own action with the cookie the page set. A list becomes the field
    /// repeated, and dates are written in ISO 8601. Throws, listing the page's forms, when none or several fit.
    /// </summary>
    public static async Task<HttpResponseMessage> SubmitFormAsync(this HttpClient client, string path, object values)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<KeyValuePair<string, string>> submitted = ValuesOf(values);
        using HttpResponseMessage page = await client.GetAsync(path, cancellationToken);
        string html = await page.Content.ReadAsStringAsync(cancellationToken);

        List<HtmlForm> forms = HtmlForm.ThatPost(html);
        List<HtmlForm> fitting = [.. forms.Where(form => HasFieldsFor(form, submitted))];
        if (fitting.Count != 1)
        {
            throw new InvalidOperationException(forms.Count == 0
                ? $"The page at {path} answered {(int)page.StatusCode} with no form that posts."
                : $"The page at {path} has {fitting.Count} forms with a field for each value: "
                    + $"{string.Join("; ", forms.Select(Describe))}. Give values for the fields of one form.");
        }

        HtmlForm form = fitting[0];
        List<KeyValuePair<string, string>> fields =
            [.. form.Values.Where(field => !submitted.Any(value => SameName(value.Key, field.Key))), .. submitted];
        Uri pageAddress = page.RequestMessage?.RequestUri ?? new Uri(client.BaseAddress!, path);
        Uri action = string.IsNullOrEmpty(form.Action) ? pageAddress : new Uri(pageAddress, form.Action);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields), cancellationToken);
    }

    internal static List<KeyValuePair<string, string>> ValuesOf(object values)
    {
        if (values is IEnumerable<KeyValuePair<string, string>> pairs)
        {
            return [.. pairs];
        }

        List<KeyValuePair<string, string>> fields = [];
        if (values is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                fields.AddRange(Fields(Convert.ToString(entry.Key, CultureInfo.InvariantCulture)!, entry.Value));
            }

            return fields;
        }

        foreach (var property in values.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length == 0)
            {
                fields.AddRange(Fields(property.Name, property.GetValue(values)));
            }
        }

        return fields;
    }

    // A list becomes the field repeated, as a multiple select or a set of checkboxes sends it.
    private static IEnumerable<KeyValuePair<string, string>> Fields(string name, object? value) =>
        value is IEnumerable items and not string
            ? items.Cast<object?>().Select(item => new KeyValuePair<string, string>(name, Text(item)))
            : [new(name, Text(value))];

    // Dates in ISO 8601, as a browser's date inputs send them, which the app reads whatever its culture.
    private static string Text(object? value) => value switch
    {
        null => "",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        DateTime or DateTimeOffset => ((IFormattable)value).ToString("o", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static bool HasFieldsFor(HtmlForm form, List<KeyValuePair<string, string>> values) =>
        values.All(value => form.FieldNames.Any(name => SameName(name, value.Key)));

    private static bool SameName(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase);

    private static string Describe(HtmlForm form) =>
        $"{form.Action ?? "(this page)"} with {string.Join(", ", form.FieldNames)}";
}
