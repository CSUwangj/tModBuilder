using System.CodeDom.Compiler;

namespace tModBuilder.Exceptions
{
  internal class BuildException : Exception
  {
    public CompilerErrorCollection compileErrors;

    public BuildException(string message) : base(message) { }

    public BuildException(string message, Exception innerException) : base(message, innerException) { }
  }
}
