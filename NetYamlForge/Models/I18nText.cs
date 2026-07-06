using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

public static class I18nText
{
    private static readonly ResourceManager SharedResourceManager =
        new("NetYamlForge.Resources.Localization.SharedResource", typeof(SharedResource).Assembly);

    public static string Resolve(Dictionary<string, string>? map, string? fallback, string? key = null)
    {
        var fromKey = ResolveByKey(key);
        if (!string.IsNullOrWhiteSpace(fromKey))
        {
            return fromKey;
        }

        return ResolveFromMap(map, fallback);
    }

    public static string? ResolveByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var culture = CultureInfo.CurrentUICulture;
        var yamlText = YamlKeyLocalizer.Resolve(key, culture, LocalizationProjectContext.CurrentProjectName);
        if (!string.IsNullOrWhiteSpace(yamlText))
        {
            return yamlText;
        }

        var exact = SafeGetResource(key, culture);
        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var neutral = culture.TwoLetterISOLanguageName;
        if (!string.IsNullOrWhiteSpace(neutral))
        {
            var neutralText = SafeGetResource(key, new CultureInfo(neutral));
            if (!string.IsNullOrWhiteSpace(neutralText))
            {
                return neutralText;
            }
        }

        var enUs = SafeGetResource(key, new CultureInfo("en-US"));
        if (!string.IsNullOrWhiteSpace(enUs))
        {
            return enUs;
        }

        var en = SafeGetResource(key, new CultureInfo("en"));
        if (!string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return null;
    }

    private static string? SafeGetResource(string key, CultureInfo culture)
    {
        try
        {
            return SharedResourceManager.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    public static string ResolveFromMap(Dictionary<string, string>? map, string? fallback)
    {
        if (map == null || map.Count == 0)
        {
            return fallback ?? string.Empty;
        }

        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (map.TryGetValue(culture, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var neutral = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var neutralKey = map.Keys.FirstOrDefault(k => k.StartsWith(neutral, StringComparison.OrdinalIgnoreCase));
        if (neutralKey != null && map.TryGetValue(neutralKey, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        if (map.TryGetValue("en-US", out var enUs) && !string.IsNullOrWhiteSpace(enUs))
        {
            return enUs;
        }

        if (map.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return fallback ?? string.Empty;
    }
}
