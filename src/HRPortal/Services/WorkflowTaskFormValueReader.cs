using Microsoft.AspNetCore.Http;

namespace HRPortal.Services;

/// <summary>
/// Extracts only a named prefix collection from an HTML form post.
/// Prevents antiforgery, routing, action and note fields from being mistaken
/// for workflow-defined fields by dictionary model binding.
/// </summary>
public static class WorkflowTaskFormValueReader
{
    public static Dictionary<string, string?> Read(IFormCollection form, string prefix)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("A form collection prefix is required.", nameof(prefix));

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var keyPrefix = prefix.Trim() + "[";

        foreach (var postedKey in form.Keys)
        {
            if (!postedKey.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) ||
                !postedKey.EndsWith(']'))
                continue;

            var fieldKey = postedKey[keyPrefix.Length..^1].Trim();
            if (fieldKey.Length == 0)
                continue;

            var postedValues = form[postedKey];
            if (postedValues.Count > 1)
                throw new InvalidOperationException($"فیلد «{fieldKey}» بیش از یک مقدار از فرم ارسال کرده است.");

            if (!result.TryAdd(fieldKey, postedValues.Count == 0 ? null : postedValues[0]))
                throw new InvalidOperationException($"فیلد «{fieldKey}» بیش از یک‌بار از فرم ارسال شده است.");
        }

        return result;
    }
}
