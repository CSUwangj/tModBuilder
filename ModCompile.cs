using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text;
using tModBuilder.Exceptions;

namespace tModBuilder
{
  // TODO further documentation
  // TODO too many inner classes
  internal class ModCompile {
    public interface IBuildStatus {
      void SetProgress(int i, int n = -1);
      void SetStatus(string msg);
      void LogCompilerLine(string msg);
    }
    
    private class ConsoleBuildStatus : IBuildStatus {
      public void SetProgress(int i, int n) { }
    
      public void SetStatus(string msg) => Console.WriteLine(msg);
    
      public void LogCompilerLine(string msg) =>Console.Error.WriteLine(msg);
    }

    private class BuildingMod : LocalMod {
      public string path;

      public BuildingMod(TmodFile modFile, BuildProperties properties, string path) : base(modFile, properties) {
        this.path = path;
      }
    }

    public static readonly string ModSourcePath = Path.Combine(Program.SavePathShared, "ModSources");

    internal static string[] FindModSources() {
      Directory.CreateDirectory(ModSourcePath);
      return Directory.GetDirectories(ModSourcePath, "*", SearchOption.TopDirectoryOnly).Where(dir => {
        var directory = new DirectoryInfo(dir);
        return directory.Name[0] != '.' && directory.Name != "ModAssemblies" && directory.Name != "Mod Libraries";
      }).ToArray();
    }

    // Silence exception reporting in the chat unless actively modding.
    public static bool activelyModding;

    public static bool DeveloperMode => Debugger.IsAttached || Directory.Exists(ModSourcePath) && FindModSources().Length > 0;

    private static readonly string tMLDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    // private static readonly string oldModReferencesPath = Path.Combine(Program.SavePath, "references");
    private static readonly string modTargetsPath = Path.Combine(ModSourcePath, "tModLoader.targets");
    private static readonly string tMLModTargetsPath = Path.Combine(tMLDir, "tMLMod.targets");
    private static bool referencesUpdated = false;
    internal static void UpdateReferencesFolder() {
      if(referencesUpdated) {
        return;
      }

      UpdateFileContents(modTargetsPath,
$@"<Project ToolsVersion=""14.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""{SecurityElement.Escape(tMLModTargetsPath)}"" />
</Project>");

      referencesUpdated = true;
    }

    private static void UpdateFileContents(string path, string contents) {
      Directory.CreateDirectory(Path.GetDirectoryName(path));

      byte[] bytes = Encoding.UTF8.GetBytes(contents);
      if(!File.Exists(path) || !Enumerable.SequenceEqual(bytes, File.ReadAllBytes(path))) {
        File.WriteAllBytes(path, bytes);
      }
    }

    internal static IList<string> sourceExtensions = new List<string> { ".csproj", ".cs", ".sln" };

    private IBuildStatus status;
    public ModCompile(IBuildStatus status) {
      this.status = status;
    
      // *gasp*, side-effects
      activelyModding = true;
    }

    internal static void BuildModCommandLine(string modFolder) {
      UpdateReferencesFolder();

      // Once we get to this point, the application is guaranteed to exit
      try {
        new ModCompile(new ConsoleBuildStatus()).Build(modFolder);
      } catch(BuildException e) {
        Console.Error.WriteLine("Error: " + e.Message);
        if(e.InnerException != null) {
          Console.Error.WriteLine(e.InnerException);
        }

        Environment.Exit(1);
      } catch(Exception e) {
        Console.Error.WriteLine(e);
        Environment.Exit(1);
      }

      // Mod was built with success, exit code 0 indicates success.
      Environment.Exit(0);
    }

    internal void Build(string modFolder) => Build(ReadBuildInfo(modFolder));

    private BuildingMod ReadBuildInfo(string modFolder) {
      if(modFolder.EndsWith("\\") || modFolder.EndsWith("/")) {
        modFolder = modFolder.Substring(0, modFolder.Length - 1);
      }

      var modName = Path.GetFileName(modFolder);
      // status.SetStatus(Language.GetTextValue("tModLoader.ReadingProperties", modName));

      BuildProperties properties;
      try {
        properties = BuildProperties.ReadBuildFile(modFolder);
      } catch(Exception e) {
        throw new BuildException("BuildErrorFailedLoadBuildTxt" + Path.Combine(modFolder, "build.txt"), e);
      }

      var file = Path.Combine(modFolder, modName + ".tmod");
      var modFile = new TmodFile(file, modName, properties.version);
      return new BuildingMod(modFile, properties, modFolder);
    }

    private void Build(BuildingMod mod) {
      try {
        // status.SetStatus(Language.GetTextValue("tModLoader.Building", mod.Name));

        BuildMod(mod, out var code, out var pdb);
        mod.modFile.AddFile(mod.Name + ".dll", code);
        if(pdb != null) {
          mod.modFile.AddFile(mod.Name + ".pdb", pdb);
        }

        PackageMod(mod);
        mod.modFile.Save();
      } catch(Exception e) {
        e.Data["mod"] = mod.Name;
        throw;
      }
    }

    private void PackageMod(BuildingMod mod) {
      status.SetStatus("Packaging" + mod);
      status.SetProgress(0, 1);

      mod.modFile.AddFile("Info", mod.properties.ToBytes());

      var resources = Directory.GetFiles(mod.path, "*", SearchOption.AllDirectories)
        .Where(res => !IgnoreResource(mod, res))
        .ToList();

      status.SetProgress(packedResourceCount = 0, resources.Count);
      Parallel.ForEach(resources, resource => AddResource(mod, resource));

      // add dll references from the -eac bin folder
      var libFolder = Path.Combine(mod.path, "lib");
      foreach(var dllPath in mod.properties.dllReferences.Select(dllName => DllRefPath(mod, dllName))) {
        if(!dllPath.StartsWith(libFolder)) {
          mod.modFile.AddFile("lib/" + Path.GetFileName(dllPath), File.ReadAllBytes(dllPath));
        }
      }
    }

    private bool IgnoreResource(BuildingMod mod, string resource) {
      var relPath = resource.Substring(mod.path.Length + 1);
      return IgnoreCompletely(mod, resource) ||
        relPath == "build.txt" ||
        !mod.properties.includeSource && sourceExtensions.Contains(Path.GetExtension(resource)) ||
        Path.GetFileName(resource) == "Thumbs.db";
    }

    // Ignore for both Compile and Packaging
    private bool IgnoreCompletely(BuildingMod mod, string resource) {
      var relPath = resource.Substring(mod.path.Length + 1);
      return mod.properties.ignoreFile(relPath) ||
        relPath[0] == '.' ||
        relPath.StartsWith("bin" + Path.DirectorySeparatorChar) ||
        relPath.StartsWith("obj" + Path.DirectorySeparatorChar);
    }

    private int packedResourceCount;
    private void AddResource(BuildingMod mod, string resource) {
      var relPath = resource.Substring(mod.path.Length + 1);
      using(var src = File.OpenRead(resource))
      using(var dst = new MemoryStream()) {
        if(!ContentConverters.Convert(ref relPath, src, dst)) {
          src.CopyTo(dst);
        }

        mod.modFile.AddFile(relPath, dst.ToArray());
        Interlocked.Increment(ref packedResourceCount);
        status.SetProgress(packedResourceCount);
      }
    }

    private List<LocalMod> FindReferencedMods(BuildProperties properties) {
      //Determine the existing mods here, then just keep passing around the collection
      // @FIXME
      //var existingMods = ModOrganizer.FindMods().ToDictionary(mod => mod.modFile.Name, mod => mod);
      //
      var mods = new Dictionary<string, LocalMod>();
      //FindReferencedMods(properties, existingMods, mods, true);
      return mods.Values.ToList();
    }

    private void FindReferencedMods(BuildProperties properties, Dictionary<string, LocalMod> existingMods, Dictionary<string, LocalMod> mods, bool requireWeak) {
      foreach(var refName in properties.RefNames(true)) {
        if(mods.ContainsKey(refName)) {
          continue;
        }

        bool isWeak = properties.weakReferences.Any(r => r.mod == refName);
        LocalMod mod;
        try {
          //If the file doesn't exist here, bail out immediately
          if(!existingMods.TryGetValue(refName, out mod)) {
            throw new FileNotFoundException($"Could not find \"{refName}.tmod\" in your subscribed Workshop mods nor the Mods folder");
          }
        } catch(FileNotFoundException) when(isWeak && !requireWeak) {
          // don't recursively require weak deps, if the mod author needs to compile against them, they'll have them installed
          continue;
        } catch(Exception ex) {
          throw new BuildException("BuildErrorModReference" + refName, ex);
        }
        mods[refName] = mod;
        FindReferencedMods(mod.properties, existingMods, mods, false);
      }
    }

    private void BuildMod(BuildingMod mod, out byte[] code, out byte[] pdb) {
      string dllName = mod.Name + ".dll";
      string dllPath = null;
      string pdbPath() => Path.ChangeExtension(dllPath, "pdb");

      // precompiled load, or fallback to Roslyn compile
      if(dllPath != null) {
        if(!File.Exists(dllPath)) {
          throw new BuildException("BuildErrorLoadingPrecompiled" + dllPath);
        }

        status.SetStatus("LoadingPrecompiled"+ dllName + Path.GetFileName(dllPath));
        code = File.ReadAllBytes(dllPath);
        pdb = File.Exists(pdbPath()) ? File.ReadAllBytes(pdbPath()) : null;
      } else {
        CompileMod(mod, out code, out pdb);
      }
    }

    private void CompileMod(BuildingMod mod, out byte[] code, out byte[] pdb) {
      status.SetStatus("tModLoader.Compiling " + mod.Name + ".dll");
      var tempDir = Path.Combine(mod.path, "compile_temp");
      if(Directory.Exists(tempDir)) {
        Directory.Delete(tempDir, true);
      }

      Directory.CreateDirectory(tempDir);

      var refs = new List<string>();

      //everything used to compile the tModLoader for the target platform
      refs.AddRange(GetTerrariaReferences());

      //libs added by the mod
      refs.AddRange(mod.properties.dllReferences.Select(dllName => DllRefPath(mod, dllName)));

      //all dlls included in all referenced mods
      foreach(var refMod in FindReferencedMods(mod.properties)) {
        using(refMod.modFile.Open()) {
          var path = Path.Combine(tempDir, refMod + ".dll");
          File.WriteAllBytes(path, refMod.modFile.GetModAssembly());
          refs.Add(path);

          foreach(var refDll in refMod.properties.dllReferences) {
            path = Path.Combine(tempDir, refDll + ".dll");
            File.WriteAllBytes(path, refMod.modFile.GetBytes("lib/" + refDll + ".dll"));
            refs.Add(path);
          }
        }
      }

      var files = Directory.GetFiles(mod.path, "*.cs", SearchOption.AllDirectories).Where(file => !IgnoreCompletely(mod, file)).ToArray();

      bool allowUnsafe = true;

      var preprocessorSymbols = new List<string> { "FNA" };

      if(BuildInfo.IsStable) {
        string tmlVersionPreprocessorSymbol = $"TML_{BuildInfo.tMLVersion.Major}_{BuildInfo.tMLVersion.Minor:D2}";
        preprocessorSymbols.Add(tmlVersionPreprocessorSymbol);
      }

      var results = RoslynCompile(mod.Name, refs, files, preprocessorSymbols.ToArray(), allowUnsafe, out code, out pdb);

      int numWarnings = results.Count(e => e.Severity == DiagnosticSeverity.Warning);
      int numErrors = results.Length - numWarnings;
      status.LogCompilerLine("tModLoader.CompilationResult" + numErrors + numWarnings);
      foreach(var line in results) {
        status.LogCompilerLine(line.ToString());
      }

      try {
        if(Directory.Exists(tempDir)) {
          Directory.Delete(tempDir, true);
        }
      } catch(Exception) { }

      if(numErrors > 0) {
        var firstError = results.First(e => e.Severity == DiagnosticSeverity.Error);
        throw new BuildException("tModLoader.CompileError" +  mod.Name + ".dll" + numErrors + numWarnings + $"\nError: {firstError}");
      }
    }

    private string DllRefPath(BuildingMod mod, string dllName) {
      string path = Path.Combine(mod.path, "lib", dllName) + ".dll";

      if(File.Exists(path)) {
        return path;
      }
      throw new BuildException("Missing dll reference: " + path);
    }

    private static IEnumerable<string> GetTerrariaReferences() {
      var executingAssembly = Assembly.GetExecutingAssembly();
      yield return executingAssembly.Location;

      // same filters as the <Reference> elements in the generated .targets file
      var libsDir = Path.Combine(Path.GetDirectoryName(executingAssembly.Location), "Libraries");
      foreach(var f in Directory.EnumerateFiles(libsDir, "*.dll", SearchOption.AllDirectories)) {
        var path = f.Replace('\\', '/');
        if(!path.EndsWith(".resources.dll") &&
          !path.Contains("/Native/") &&
          !path.Contains("/runtime")) {
          yield return f;
        }
      }
    }

    /// <summary>
    /// Compile a dll for the mod based on required includes.
    /// </summary>
    private static Diagnostic[] RoslynCompile(string name, List<string> references, string[] files, string[] preprocessorSymbols, bool allowUnsafe, out byte[] code, out byte[] pdb) {
      var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
        assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default,
        optimizationLevel: preprocessorSymbols.Contains("DEBUG") ? OptimizationLevel.Debug : OptimizationLevel.Release,
        allowUnsafe: allowUnsafe);

      var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: preprocessorSymbols);

      var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);

      var refs = references.Select(s => MetadataReference.CreateFromFile(s));
      refs = refs.Concat(Net60.All);

      var src = files.Select(f => SyntaxFactory.ParseSyntaxTree(File.ReadAllText(f), parseOptions, f, Encoding.UTF8));

      var comp = CSharpCompilation.Create(name, src, refs, options);

      using var peStream = new MemoryStream();
      using var pdbStream = new MemoryStream();
      var results = comp.Emit(peStream, pdbStream, options: emitOptions);

      code = peStream.ToArray();
      pdb = pdbStream.ToArray();
      return results.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).ToArray();
    }
  }
}
