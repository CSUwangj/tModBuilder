namespace tModBuilder.Exceptions
{
  class ResourceLoadException : Exception
  {
    public ResourceLoadException(string message, Exception inner = null)
      : base(message, inner) {
    }
  }
}
