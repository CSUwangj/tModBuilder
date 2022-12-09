﻿namespace tModBuilder
{
    internal class LocalMod {
    public readonly TmodFile modFile;
    public readonly BuildProperties properties;
    public DateTime lastModified;

    public string Name => modFile.Name;
    public string DisplayName => string.IsNullOrEmpty(properties.displayName) ? Name : properties.displayName;
    public Version tModLoaderVersion => properties.buildVersion;

    public bool Enabled => true;
    public override string ToString() => Name;

    public LocalMod(TmodFile modFile, BuildProperties properties) {
      this.modFile = modFile;
      this.properties = properties;
    }

    public LocalMod(TmodFile modFile) : this(modFile, BuildProperties.ReadModFile(modFile)) {
    }
  }
}
