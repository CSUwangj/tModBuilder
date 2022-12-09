namespace tModBuilder
{
  internal static class ContentConverters
  {
    internal static bool Convert(ref string resourceName, FileStream src, MemoryStream dst) {
      switch (Path.GetExtension(resourceName).ToLower()) {
        case ".png":
          if (resourceName != "icon.png" && ImageIO.ToRaw(src, dst)) {
            resourceName = Path.ChangeExtension(resourceName, "rawimg");
            return true;
          }
          src.Position = 0;
          return false;
        default:
          return false;
      }
    }

    internal static bool Reverse(ref string resourceName, out Action<Stream, Stream> converter) {
      if(resourceName == "Info") {
        resourceName = "build.txt";
        converter = BuildProperties.InfoToBuildTxt;
        return true;
      }
      switch (Path.GetExtension(resourceName).ToLower()) {
        case ".rawimg":
          throw new Exception("Raw Image is not permitted in tModBuilder");
        default:
          converter = null;
          return false;
      }
    }
  }
}
