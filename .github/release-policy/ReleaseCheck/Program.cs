// Release check for the GUI state data, run by the "Smoke test linux-x64 (native)" job of
// .github/workflows/release.yml (docs/ai-context/build-and-test.md §Release workflow).
//
//   ReleaseCheck <published app directory> <CampertronConfig directory>
//
// It loads CampertronLibrary.dll and its dependencies from the extracted release archive (not from the
// sources), and runs that library's own queries against RecreationDotOrg.db in the config directory, which
// the published application extracted from its embedded database. It checks the "By City" and "By State"
// data the GUI shows and the refresh validation. Exit code 0 when every check passes, 1 otherwise.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: ReleaseCheck <published app directory> <CampertronConfig directory>");
    return 2;
}
string AppDir = Path.GetFullPath(args[0]);
string ConfigDir = Path.GetFullPath(args[1]);
string DbFile = Path.Combine(ConfigDir, "RecreationDotOrg.db");
if (!File.Exists(Path.Combine(AppDir, "CampertronLibrary.dll")) || !File.Exists(DbFile))
{
    Console.Error.WriteLine($"::error::ReleaseCheck: {AppDir}/CampertronLibrary.dll or {DbFile} is missing");
    return 1;
}

// Resolve managed and native dependencies from the published directory only.
var Context = new AssemblyLoadContext("published", isCollectible: false);
Context.Resolving += (ctx, name) =>
{
    string Candidate = Path.Combine(AppDir, name.Name + ".dll");
    return File.Exists(Candidate) ? ctx.LoadFromAssemblyPath(Candidate) : null;
};
Context.ResolvingUnmanagedDll += (assembly, name) =>
{
    string Rid = RuntimeInformation.RuntimeIdentifier;
    foreach (string Dir in new[] { AppDir, Path.Combine(AppDir, "runtimes", Rid, "native") })
    {
        foreach (string File_ in new[] { name, "lib" + name + ".so", name + ".so", "lib" + name + ".dylib", name + ".dll" })
        {
            string Candidate = Path.Combine(Dir, File_);
            if (File.Exists(Candidate))
            {
                return NativeLibrary.Load(Candidate);
            }
        }
    }
    return IntPtr.Zero;
};

Assembly Library = Context.LoadFromAssemblyPath(Path.Combine(AppDir, "CampertronLibrary.dll"));
Console.WriteLine($"Published library: {Library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
Console.WriteLine($"Database: {DbFile} ({new FileInfo(DbFile).Length} bytes)");

Type Read = Required(Library, "CampertronLibrary.function.RecDotOrg.sqlite.Read");
Type Legacy = Required(Library, "CampertronLibrary.function.RecDotOrg.sqlite.LegacyAddressLayout");
Type Ridb = Required(Library, "CampertronLibrary.function.RecDotOrg.sqlite.RidbDatabase");

List<string> Query(string Method, params object[] Args) =>
    (List<string>)Read.GetMethods()
        .Single(m => m.Name == Method && m.GetParameters().Length == Args.Length && m.GetParameters().All(p => p.ParameterType == typeof(string)))
        .Invoke(null, Args)!;

var Failures = new List<string>();
void Check(bool Ok, string Description, string Detail)
{
    Console.WriteLine($"{(Ok ? "PASS" : "FAIL")}  {Description}{(Ok ? "" : ": " + Detail)}");
    if (!Ok) Failures.Add(Description + ": " + Detail);
}
string Show(List<string> Values) => Values.Count == 0 ? "(empty)" : string.Join(",", Values.Take(10)) + (Values.Count > 10 ? $",... ({Values.Count} values)" : "");

var Problems = (List<string>)Ridb.GetMethod("Validate")!.Invoke(null, new object[] { ConfigDir })!;
Check(Problems.Count == 0, "RidbDatabase.Validate passes", string.Join(" | ", Problems));
object Layout = Legacy.GetMethod("DetectLayout")!.Invoke(null, new object[] { DbFile })!;
Check(Layout.ToString() == "Corrected", "LegacyAddressLayout.DetectLayout is Corrected", Layout.ToString()!);
bool OnlyKnown = (bool)Legacy.GetMethod("HasOnlyKnownSchemaObjects")!.Invoke(null, new object[] { DbFile })!;
Check(OnlyKnown, "database contains only Campertron's own schema objects", "unknown tables, views, triggers or indexes");

List<string> States = Query("UniqueStates", ConfigDir);
Check(States.Count > 0, "state list is not empty", "no states");
var NotTwoLetter = States.Where(s => !Regex.IsMatch(s, "^[A-Z]{2}$")).ToList();
Check(NotTwoLetter.Count == 0, "every state is a two-letter abbreviation", Show(NotTwoLetter));
var ZipLike = States.Where(s => Regex.IsMatch(s, "[0-9]")).ToList();
Check(ZipLike.Count == 0, "no ZIP code appears as a state", Show(ZipLike));
Check(!States.Contains("01005"), "01005 is not a state", "01005 is in the state list");
var Missing = new[] { "AZ", "MA", "UT" }.Where(s => !States.Contains(s)).ToList();
Check(Missing.Count == 0, "AZ, MA and UT are states", "missing " + string.Join(",", Missing));

List<string> MaCities = Query("UniqueCities", "MA", ConfigDir);
Check(MaCities.Contains("BARRE"), "By City: MA lists BARRE", Show(MaCities));
List<string> MaCampgrounds = Query("UniqueParksByState", "MA", ConfigDir);
Check(MaCampgrounds.Count > 0, "By State: MA lists campgrounds", "(empty)");
List<string> ZipCities = Query("UniqueCities", "01005", ConfigDir);
Check(ZipCities.Count == 0, "By City: 01005 lists no city", Show(ZipCities));
List<string> ZipCampgrounds = Query("UniqueParksByState", "01005", ConfigDir);
Check(ZipCampgrounds.Count == 0, "By State: 01005 lists no campground", Show(ZipCampgrounds));
List<string> Springdale = Query("UniqueParks", "UT", "SPRINGDALE", ConfigDir);
Check(Springdale.Contains("WATCHMAN CAMPGROUND"), "UT / SPRINGDALE lists WATCHMAN CAMPGROUND", Show(Springdale));

Console.WriteLine($"{States.Count} states: {string.Join(",", States)}");
if (Failures.Count > 0)
{
    foreach (string Failure in Failures)
    {
        Console.WriteLine($"::error::GUI state data check failed: {Failure}");
    }
    return 1;
}
Console.WriteLine("GUI state data checks passed.");
return 0;

static Type Required(Assembly Library, string Name) =>
    Library.GetType(Name) ?? throw new InvalidOperationException($"{Name} not found in the published CampertronLibrary.dll");
