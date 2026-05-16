using Rhino.Collections;
using Rhino.Compute;
using RhMcp.Compute;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ComputePythonTool
{
    [McpServerTool(Name = "compute_python")]
    [Description("Evaluate a Python 3 script on a Rhino Compute server (NOT in the current Rhino doc). Use this for headless geometry work that doesn't need the live document. `inputs` is a JSON object whose top-level fields become script variables; the script's output dictionary is returned as JSON in `outputs`. Returns JSON with serverUrl, outputs, and error fields; error is null on success. Server URL comes from RHINO_COMPUTE_URL (defaults to http://localhost:6500).")]
    public static string ComputePython(
        [Description("Python 3 script. Read inputs by name (e.g. `radius`) and assign outputs by name on the implicit output dict.")] string script,
        [Description("Optional JSON object of input variables. Top-level fields become script inputs; supported value types: number, bool, string, null, and arrays of those.")] string? inputs = null)
    {
        ComputeConfig.EnsureInitialized();
        ArchivableDictionary inDict;
        try
        {
            inDict = BuildInputs(inputs);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Bad inputs JSON: {ex.Message}", serverUrl = ComputeConfig.CurrentUrl });
        }

        try
        {
            var outDict = PythonCompute.Evaluate(script, inDict);
            var outputs = DictToObject(outDict);
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs, error = (string?)null });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { serverUrl = ComputeConfig.CurrentUrl, outputs = (object?)null, error = ex.Message });
        }
    }

    private static ArchivableDictionary BuildInputs(string? json)
    {
        var dict = new ArchivableDictionary();
        if (string.IsNullOrWhiteSpace(json)) return dict;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("inputs must be a JSON object");

        foreach (var prop in doc.RootElement.EnumerateObject())
            SetFromJson(dict, prop.Name, prop.Value);

        return dict;
    }

    private static void SetFromJson(ArchivableDictionary dict, string key, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                dict.Set(key, value.GetString());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                dict.Set(key, value.GetBoolean());
                break;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var i)) dict.Set(key, i);
                else dict.Set(key, value.GetDouble());
                break;
            case JsonValueKind.Null:
                break;
            case JsonValueKind.Array:
                SetArray(dict, key, value);
                break;
            default:
                // Nested objects aren't a great fit for ArchivableDictionary's typed slots —
                // stash the raw JSON so the script can re-parse if it needs to.
                dict.Set(key, value.GetRawText());
                break;
        }
    }

    private static void SetArray(ArchivableDictionary dict, string key, JsonElement array)
    {
        var len = array.GetArrayLength();
        if (len == 0) { dict.Set(key, Array.Empty<string>()); return; }

        var first = array[0].ValueKind;
        if (first == JsonValueKind.Number && AllNumbers(array, out var allInt))
        {
            if (allInt) dict.Set(key, array.EnumerateArray().Select(e => e.GetInt32()).ToArray());
            else dict.Set(key, array.EnumerateArray().Select(e => e.GetDouble()).ToArray());
            return;
        }
        if (first is JsonValueKind.True or JsonValueKind.False && All(array, JsonValueKind.True, JsonValueKind.False))
        {
            dict.Set(key, array.EnumerateArray().Select(e => e.GetBoolean()).ToArray());
            return;
        }

        // Mixed or string array → stringify each element so the script gets something useful.
        dict.Set(key, array.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())
            .ToArray());
    }

    private static bool AllNumbers(JsonElement array, out bool allInt)
    {
        allInt = true;
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Number) { allInt = false; return false; }
            if (!e.TryGetInt32(out _)) allInt = false;
        }
        return true;
    }

    private static bool All(JsonElement array, params JsonValueKind[] kinds)
    {
        foreach (var e in array.EnumerateArray())
            if (Array.IndexOf(kinds, e.ValueKind) < 0) return false;
        return true;
    }

    private static Dictionary<string, object?> DictToObject(ArchivableDictionary dict)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (dict is null) return result;
        foreach (var key in dict.Keys)
            result[key] = dict[key];
        return result;
    }
}
