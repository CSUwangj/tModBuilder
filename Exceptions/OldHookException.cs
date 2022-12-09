namespace tModBuilder.Exceptions
{
  public class OldHookException : Exception
  {
    public OldHookException(string hook)
      : base("This mod uses an old " + hook + " hook") {
    }
  }
}
