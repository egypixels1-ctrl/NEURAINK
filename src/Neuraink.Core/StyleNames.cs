namespace Neuraink.Core;

/// <summary>The names of the script styles: a library folder (diwani) and how it reads (ديواني).</summary>
public static class StyleNames
{
    private static readonly Dictionary<string, (string Ar, string En)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["diwani"] = ("ديواني", "Diwani"), ["diwani_jali"] = ("ديواني جلي", "Diwani Jali"), ["thuluth"] = ("ثلث", "Thuluth"),
        ["thulth"] = ("ثلث", "Thuluth"), ["naskh"] = ("نسخ", "Naskh"), ["ruqaa"] = ("رقعة", "Ruq'ah"), ["ruqah"] = ("رقعة", "Ruq'ah"),
        ["farsi"] = ("فارسي", "Farsi"), ["nastaliq"] = ("نستعليق", "Nastaliq"), ["kufi"] = ("كوفي", "Kufi"),
        ["maghribi"] = ("مغربي", "Maghribi"), ["ijaza"] = ("إجازة", "Ijazah"), ["free"] = ("حر", "Free"),
    };

    /// <summary>The Arabic name of a style folder (the folder name itself when unknown, or when it is Arabic already).</summary>
    public static string Arabic(string folder) => Known.TryGetValue(folder ?? "", out var n) ? n.Ar : folder ?? "";

    public static string English(string folder) => Known.TryGetValue(folder ?? "", out var n) ? n.En : folder ?? "";
}
