namespace Neuraink.Core;

public enum LetterForm { Isolated, Initial, Medial, Final }

/// <summary>A letter of the text with its position form (what the segmentation walks through).</summary>
public sealed record LetterChip(int Index, string Ch, int Word, LetterForm Form)
{
    /// <summary>The join from the previous letter enters this one.</summary>
    public bool NeedsEntry => Form is LetterForm.Medial or LetterForm.Final;
    /// <summary>This letter joins to the next one.</summary>
    public bool NeedsExit => Form is LetterForm.Medial or LetterForm.Initial;
}

public static class Arabic
{
    /// <summary>Letters that never join to the following letter.</summary>
    private const string NonLeftJoining = "اأإآٱدذرزوؤةء";

    public static bool IsArabicLetter(char c) => c is >= 'ء' and <= 'ي' or 'ٱ' or (>= 'پ' and <= 'ۓ');

    /// <summary>Removes tashkeel and tatweel.</summary>
    public static string Clean(string text) =>
        new((text ?? "").Where(c => !(c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ')).ToArray());

    public static List<LetterChip> Letters(string text)
    {
        var list = new List<LetterChip>();
        var words = Clean(text).Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int index = 0;
        for (int w = 0; w < words.Length; w++)
        {
            var ch = words[w].Where(IsArabicLetter).ToArray();
            for (int i = 0; i < ch.Length; i++)
            {
                char c = ch[i];
                bool joinPrev = i > 0 && !NonLeftJoining.Contains(ch[i - 1]) && c != 'ء';
                bool joinNext = i < ch.Length - 1 && !NonLeftJoining.Contains(c) && ch[i + 1] != 'ء';
                var form = joinPrev && joinNext ? LetterForm.Medial : joinPrev ? LetterForm.Final : joinNext ? LetterForm.Initial : LetterForm.Isolated;
                list.Add(new LetterChip(index++, c.ToString(), w, form));
            }
        }
        return list;
    }

    public static string FormKey(LetterForm f) => f switch
    {
        LetterForm.Initial => "initial", LetterForm.Medial => "medial", LetterForm.Final => "final", _ => "isolated",
    };

    public static LetterForm ParseForm(string? s) => s switch
    {
        "initial" => LetterForm.Initial, "medial" => LetterForm.Medial, "final" => LetterForm.Final, _ => LetterForm.Isolated,
    };

    /// <summary>Folder name of a letter in the letter library: its code point(s) in hex ("0645").</summary>
    public static string Code(string letter) => string.Join("-", letter.Select(c => ((int)c).ToString("X4")));

    public static string FromCode(string code)
    {
        try { return new string(code.Split('-').Select(x => (char)Convert.ToInt32(x, 16)).ToArray()); }
        catch { return ""; }
    }
}
