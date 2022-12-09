using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

namespace tModBuilder
{
	//todo: further documentation
	public static class AssemblyManager
	{
		private class ModLoadContext : AssemblyLoadContext
		{
			public readonly TmodFile modFile;
			public readonly BuildProperties properties;

			public List<ModLoadContext> dependencies = new List<ModLoadContext>();

			public Assembly assembly;
			public IDictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>();
			public IDictionary<string, byte[]> assemblyBytes = new Dictionary<string, byte[]>();
			public IDictionary<Assembly, Type[]> loadableTypes = new Dictionary<Assembly, Type[]>();
			public long bytesLoaded = 0;

			public ModLoadContext(LocalMod mod) : base(mod.Name, true) {
				modFile = mod.modFile;
				properties = mod.properties;

				Unloading += ModLoadContext_Unloading;
			}

			private void ModLoadContext_Unloading(AssemblyLoadContext obj) {
				// required for this to actually unload
				dependencies = null;
				assembly = null;
				assemblies = null;
				loadableTypes = null;
			}

			public void AddDependency(ModLoadContext dep) {
				dependencies.Add(dep);
			}

			protected override Assembly Load(AssemblyName assemblyName) {
				if (assemblies.TryGetValue(assemblyName.Name, out var asm))
					return asm;

				return dependencies.Select(dep => dep.Load(assemblyName)).FirstOrDefault(a => a != null);
			}

			internal bool IsModDependencyPresent(string name) => name == Name || dependencies.Any(d => d.IsModDependencyPresent(name));


			private class MetadataResolver : MetadataAssemblyResolver
			{
				private readonly ModLoadContext mod;

				public MetadataResolver(ModLoadContext mod) {
					this.mod = mod;
				}

				public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName) {
					var existing = context.GetAssemblies().SingleOrDefault(a => a.GetName().FullName == assemblyName.FullName);
					if (existing != null)
						return existing;

					var runtime = mod.LoadFromAssemblyName(assemblyName);
					if (string.IsNullOrEmpty(runtime.Location))
						return context.LoadFromByteArray(((ModLoadContext)GetLoadContext(runtime)).assemblyBytes[assemblyName.Name]);


					return context.LoadFromAssemblyPath(runtime.Location);
				}
			}

			internal void ClearAssemblyBytes() {
				assemblyBytes.Clear();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static void Unload() {
			foreach (var alc in loadedModContexts.Values) {
				oldLoadContexts.Add(new WeakReference<AssemblyLoadContext>(alc));
				alc.Unload();
			}

			hostContextForAssembly.Clear();
			loadedModContexts.Clear();

			for (int i = 0; i < 10; i++) {
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}

		internal static IEnumerable<string> OldLoadContexts() {
			foreach (var alcRef in oldLoadContexts)
				if (alcRef.TryGetTarget(out var alc))
					yield return alc.Name;
		}

		private static readonly List<WeakReference<AssemblyLoadContext>> oldLoadContexts = new();

		private static readonly Dictionary<string, ModLoadContext> loadedModContexts = new();
		private static readonly Dictionary<Assembly, ModLoadContext> hostContextForAssembly = new();

		//private static CecilAssemblyResolver cecilAssemblyResolver = new CecilAssemblyResolver();

		private static bool assemblyResolverAdded;

    private static Assembly TmlCustomResolver(object sender, ResolveEventArgs args) {
			//Legacy: With FNA and .Net5 changes, had aimed to eliminate the variants of tmodloader (tmodloaderdebug, tmodloaderserver) and Terraria as assembly names.
			// However, due to uncertainty in that elimination, in particular for Terraria, have opted to retain the original check. - Solxan
			var name = new AssemblyName(args.Name).Name;
			if (name.Contains("tModLoader") || name == "Terraria")
				return Assembly.GetExecutingAssembly();

			if (name == "FNA")
				return typeof(Vector2).Assembly;

			return null;
		}

		private static string GetModAssemblyFileName(this TmodFile modFile) => $"{modFile.Name}.dll";

		public static byte[] GetModAssembly(this TmodFile modFile) => modFile.GetBytes(modFile.GetModAssemblyFileName());

		public static byte[] GetModPdb(this TmodFile modFile) => modFile.GetBytes(Path.ChangeExtension(modFile.GetModAssemblyFileName(), "pdb"));

	}
}
