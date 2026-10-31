using System.Net;
using System.Text.RegularExpressions;

namespace Fixtura;

// A page's form as a browser would submit it: where it posts, the names of its fields, and the values it would send
// untouched, such as a prefilled input's, a checked checkbox's or the antiforgery token.
internal sealed partial record HtmlForm(string? Action, List<string> FieldNames, List<KeyValuePair<string, string>> Values)
{
    // Only forms that post carry an antiforgery token; a form without a method sends a GET.
    public static List<HtmlForm> ThatPost(string html)
    {
        List<HtmlForm> forms = [];
        foreach (Match form in FormPattern().Matches(html))
        {
            Dictionary<string, string> attributes = Attributes(form.Groups["attributes"].Value);
            if (attributes.GetValueOrDefault("method")?.Equals("post", StringComparison.OrdinalIgnoreCase) == true)
            {
                forms.Add(Read(attributes.GetValueOrDefault("action"), form.Groups["body"].Value));
            }
        }

        return forms;
    }

    private static HtmlForm Read(string? action, string body)
    {
        List<string> names = [];
        List<KeyValuePair<string, string>> values = [];
        foreach (Match field in FieldPattern().Matches(body))
        {
            string attributesText = field.Groups["attributes"].Value;
            Dictionary<string, string> attributes = Attributes(attributesText);
            if (!attributes.TryGetValue("name", out string? name) || HasFlag(attributesText, "disabled"))
            {
                continue;
            }

            names.Add(name);
            foreach (string value in ValuesOf(field, attributes, attributesText))
            {
                values.Add(new(name, value));
            }
        }

        return new HtmlForm(action, [.. names.Distinct()], values);
    }

    // What a browser sends for a field nobody touched.
    private static IEnumerable<string> ValuesOf(Match field, Dictionary<string, string> attributes, string attributesText)
    {
        string tag = field.Groups["tag"].Value.ToLowerInvariant();
        if (tag == "textarea")
        {
            // A browser drops the line break that follows the opening tag, which ASP.NET writes there.
            string text = WebUtility.HtmlDecode(field.Groups["content"].Value);
            return [text.StartsWith("\r\n") ? text[2..] : text.StartsWith('\n') ? text[1..] : text];
        }

        if (tag == "select")
        {
            return SelectedOptions(field.Groups["content"].Value, HasFlag(attributesText, "multiple"));
        }

        string type = attributes.GetValueOrDefault("type")?.ToLowerInvariant() ?? "text";
        return type switch
        {
            "submit" or "button" or "reset" or "image" or "file" => [],
            "checkbox" or "radio" when HasFlag(attributesText, "checked") => [attributes.GetValueOrDefault("value") ?? "on"],
            "checkbox" or "radio" => [],
            _ => [attributes.GetValueOrDefault("value") ?? ""]
        };
    }

    // A single select with nothing selected sends its first option.
    private static IEnumerable<string> SelectedOptions(string options, bool multiple)
    {
        List<(string Value, bool Selected)> all = [];
        foreach (Match option in OptionPattern().Matches(options))
        {
            string attributesText = option.Groups["attributes"].Value;
            string value = Attributes(attributesText).GetValueOrDefault("value")
                ?? WebUtility.HtmlDecode(option.Groups["text"].Value).Trim();
            all.Add((value, HasFlag(attributesText, "selected")));
        }

        List<string> selected = [.. all.Where(option => option.Selected).Select(option => option.Value)];
        return selected.Count > 0 || multiple || all.Count == 0 ? selected : [all[0].Value];
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in AttributePattern().Matches(text))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["value"].Value));
        }

        return attributes;
    }

    // An attribute such as checked, bare or with a value, as ASP.NET writes it: checked="checked".
    private static bool HasFlag(string attributesText, string flag)
    {
        string unquoted = AttributePattern().Replace(attributesText, " ");
        return Attributes(attributesText).ContainsKey(flag)
            || Regex.IsMatch(unquoted, $@"(?:^|\s){flag}(?:\s|/|$)", RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"<form\b(?<attributes>[^>]*)>(?<body>.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FormPattern();

    // Inputs, text areas and selects, in the order they appear, as a browser sends them.
    [GeneratedRegex(
        @"<(?<tag>input)\b(?<attributes>[^>]*)>|<(?<tag>textarea|select)\b(?<attributes>[^>]*)>(?<content>.*?)</\k<tag>>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"<option\b(?<attributes>[^>]*)>(?<text>.*?)(?=</option>|<option\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OptionPattern();

    [GeneratedRegex(@"(?<name>[\w-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))")]
    private static partial Regex AttributePattern();
}
