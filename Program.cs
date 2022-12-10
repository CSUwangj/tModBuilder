using static tModBuilder.ModCompile;

namespace tModBuilder {
  internal class Program {

    static void Main(string[] args) {
      Console.WriteLine($"Building mod at {args[0]}");
      BuildModCommandLine(args[0]);
    }
  }
}