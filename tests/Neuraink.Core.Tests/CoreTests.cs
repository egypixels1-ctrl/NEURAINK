using Neuraink.Core;

public class ArabicTests
{
    private static string Forms(string text) =>
        string.Join(" ", Arabic.Letters(text).Select(c => c.Ch + ":" + Arabic.FormKey(c.Form)));

    [Fact]
    public void Muhammad() =>
        Assert.Equal("م:initial ح:medial م:medial د:final", Forms("محمد"));

    [Fact]
    public void NonJoiningLetters() =>
        Assert.Equal("ا:isolated ح:initial م:medial د:final", Forms("احمد"));

    [Fact]
    public void WordsAndTashkeel()
    {
        var l = Arabic.Letters("عَبْد الله");
        Assert.Equal(7, l.Count);
        Assert.Equal(0, l[2].Word);
        Assert.Equal(1, l[3].Word);
        Assert.Equal(LetterForm.Isolated, l[3].Form);   // ا
        Assert.Equal(LetterForm.Final, l[6].Form);      // ه
    }

    [Fact]
    public void EntryAndExit()
    {
        var l = Arabic.Letters("محمد");
        Assert.False(l[0].NeedsEntry); Assert.True(l[0].NeedsExit);
        Assert.True(l[3].NeedsEntry); Assert.False(l[3].NeedsExit);
    }

    [Fact]
    public void CodeRoundTrip() => Assert.Equal("م", Arabic.FromCode(Arabic.Code("م")));
}

public class LibraryTests
{
    private const string Report =
        "﻿id,file,folder,status,text,tier,width,height,duplicate_of,similar_to,clean_png,svg,thumb,note\n" +
        "1,Gallery\\محمد.jpg,Gallery,clean,محمد,,800,800,,,clean_png/00001_محمد.png,svg/00001_محمد.svg,thumbs/00001_محمد.jpg,\n" +
        "2,\"Gallery\\ابتسام, الغالية.jpg\",Gallery,needs_cleaning,ابتسام الغالية,hall_of_fame,800,800,,,clean_png\\00002_x.png,,,\n" +
        "3,Gallery\\a.jpg,Gallery,complex,a,,1,1,,,,,,\n" +
        "4,Gallery\\محمد 2.jpg,Gallery,inverted,محمد,,800,800,,,clean_png/00004_محمد.png,,,\n";

    [Fact]
    public void ParsesUsableRows()
    {
        var items = Library.Parse(Report);
        Assert.Equal(3, items.Count);
        Assert.Equal("Gallery\\ابتسام, الغالية.jpg", items[1].File);
        Assert.Equal("clean_png/00002_x.png", items[1].Png);
        Assert.Equal(2, items[1].Words);
    }

    [Fact]
    public void Frequencies()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neuraink-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "report.csv"), Report);
        var lib = new Library(dir);
        Assert.True(lib.Found);
        Assert.Equal(2, lib.Get(1)!.Freq);
        Assert.Equal(1, lib.Get(2)!.Freq);
    }

    [Fact]
    public void AnnotationRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neuraink-test-" + Guid.NewGuid().ToString("N"));
        var store = new AnnotationStore(Path.Combine(dir, "annotations"));
        store.Save(new Annotation
        {
            Id = 7, Text = "محمد", State = "done",
            Segments = [new Segment { Kind = "letter", Chip = 0, Letter = "م", Form = "initial", Points = [[1, 2], [3, 4], [5, 6]], Exit = [3, 3] }],
        });
        var again = new AnnotationStore(Path.Combine(dir, "annotations"));
        Assert.Equal("done", again.State(7));
        var a = again.Load(7)!;
        Assert.Equal("م", a.Segments[0].Letter);
        Assert.Equal(3, a.Segments[0].Points.Count);
        Assert.Contains("محمد", File.ReadAllText(Path.Combine(dir, "annotations", "00007.json")));
    }

    [Fact]
    public void ReadsBrowserVersionFormat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neuraink-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "annotations"));
        File.WriteAllText(Path.Combine(dir, "annotations", "01513.json"),
            "{\"text\":\"سيما\",\"state\":\"done\",\"segments\":[{\"kind\":\"letter\",\"chip\":0,\"letter\":\"س\",\"form\":\"initial\",\"points\":[[100,50],[250,50],[250,300]],\"entry\":null,\"exit\":[240,200]}],\"id\":1513}");
        var store = new AnnotationStore(Path.Combine(dir, "annotations"));
        Assert.Equal("done", store.State(1513));
        Assert.Equal(new[] { 240.0, 200.0 }, store.Load(1513)!.Segments[0].Exit);
    }
}

public class DatabaseTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "neuraink-db-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreatesOnlyWhatIsMissing()
    {
        var root = Temp();
        Directory.CreateDirectory(Path.Combine(root, "Letters"));
        File.WriteAllText(Path.Combine(root, "Letters", "keep.txt"), "x");
        var db = new NeurainkDatabase(root);
        Assert.Contains("Library\\", db.Missing());
        Assert.DoesNotContain("Letters\\", db.Missing());
        db.CreateMissing();
        Assert.True(db.IsComplete);
        Assert.Equal("x", File.ReadAllText(Path.Combine(root, "Letters", "keep.txt")));
    }

    [Fact]
    public void FindsTheLibraryOfAStyle()
    {
        var root = Temp();
        var db = new NeurainkDatabase(root);
        db.CreateMissing();
        var style = Path.Combine(root, "Library", "ديواني");
        Directory.CreateDirectory(style);
        File.WriteAllText(Path.Combine(style, "report.csv"), "id\n");
        Assert.Equal(style, db.LibraryFolder("ديواني"));
        Assert.Equal(style, db.LibraryFolder("ثلث"));   // only style: used
        Assert.Equal(new[] { "ديواني" }, db.Styles());
    }

    [Fact]
    public void FindsAnOlderLibraryNextToIt()
    {
        var root = Temp();
        var old = Path.Combine(root, "diwani");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "report.csv"), "id\n");
        Assert.Equal(old, new NeurainkDatabase(root).LibraryFolder("ديواني"));
    }

    [Fact]
    public void OldSettingsBecomeADatabase()
    {
        var s = new AppSettings { LibraryPath = @"C:\CalligraphyAI\diwani" };
        s.FillDefaults();
        Assert.Equal(@"C:\CalligraphyAI", s.DatabasePath);
        Assert.Null(s.LibraryPath);
    }
}

public class VersioningTests
{
    [Fact]
    public void SplitIsStableAndByText()
    {
        Assert.Equal(DataVersion.SplitFor("محمد"), DataVersion.SplitFor("مُحَمَّد"));
        var texts = Enumerable.Range(0, 2000).Select(i => "اسم" + i).ToList();
        int test = texts.Count(t => DataVersion.SplitFor(t) == "test");
        Assert.InRange(test, 120, 280);   // about one in ten
    }

    [Fact]
    public void SavedSamplesCarryTheirVersions()
    {
        DataVersion.Engine = "9.9.9";
        var dir = Path.Combine(Path.GetTempPath(), "neuraink-v-" + Guid.NewGuid().ToString("N"));
        var store = new AnnotationStore(dir);
        store.Save(new Annotation { Id = 1, Text = "محمد", State = "done" });
        var a = store.Load(1)!;
        Assert.Equal(DataVersion.Schema, a.Schema);
        Assert.Equal("9.9.9", a.Engine);
        Assert.Equal(DataVersion.SplitFor("محمد"), a.Split);
    }
}
