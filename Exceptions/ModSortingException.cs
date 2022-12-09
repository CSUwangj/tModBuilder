namespace tModBuilder.Exceptions
{
  internal class ModSortingException : Exception
  {
    public ICollection<LocalMod> errored;

    public ModSortingException(ICollection<LocalMod> errored, string message) : base(message) {
      this.errored = errored;
    }
  }
}