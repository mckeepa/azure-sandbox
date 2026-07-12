namespace OnPrem_CSharp_WebApp.Controllers;

public sealed class KeyVaultPageView
{
    private readonly string _template;

    public KeyVaultPageView(IHostEnvironment environment)
    {
        string contentRootPath = environment.ContentRootPath;
        string baseDirectory = AppContext.BaseDirectory;
        _template = LoadTemplateHtml(contentRootPath, baseDirectory);
    }

    public string Render(string? selectedSecretName, IReadOnlyList<string> secretNames, string? message, string? secretValue = null, IReadOnlyDictionary<string, string>? listedSecrets = null)
    {
        string resultMarkup = string.IsNullOrWhiteSpace(secretValue)
            ? string.Empty
            : $"<div class=\"result\"><h3>Secret value</h3><pre>{Html(secretValue)}</pre></div>";

        string listMarkup = listedSecrets is null || listedSecrets.Count == 0
            ? string.Empty
            : $"<div class=\"result\"><h3>Accessible secrets</h3><ul>{string.Join(Environment.NewLine, listedSecrets.Select(secret => $"<li><strong>{Html(secret.Key)}</strong>: {Html(secret.Value)}</li>"))}</ul></div>";

        string statusMarkup = string.IsNullOrWhiteSpace(message)
            ? "<p class=\"status\">Ready to read a secret.</p>"
            : $"<p class=\"status\">{Html(message)}</p>";

        return _template
            .Replace("__STATUS__", statusMarkup)
            .Replace("__SELECTED_SECRET__", Html(selectedSecretName))
            .Replace("__LIST_MARKUP__", listMarkup)
            .Replace("__RESULT_MARKUP__", resultMarkup);
    }

    private static string LoadTemplateHtml(string contentRootPath, string baseDirectory)
    {
        string[] candidatePaths =
        [
            Path.Combine(contentRootPath, "Views", "key-vault-page.html"),
            Path.Combine(baseDirectory, "Views", "key-vault-page.html")
        ];

        foreach (string candidatePath in candidatePaths)
        {
            if (File.Exists(candidatePath))
            {
                return File.ReadAllText(candidatePath);
            }
        }

        throw new FileNotFoundException("Unable to locate the key-vault page template.", Path.Combine(contentRootPath, "Views", "key-vault-page.html"));
    }

    private static string Html(string? value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
    }
}
