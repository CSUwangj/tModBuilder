using static tModBuilder.ModCompile;

namespace tModBuilder {
  internal class Program {
    public static string SavePathShared { get; private set; } // Points to the Stable tModLoader save folder, used for Mod Sources only currently

    static void Main(string[] args) {
      BuildModCommandLine(args[0]);
    }
  }
}