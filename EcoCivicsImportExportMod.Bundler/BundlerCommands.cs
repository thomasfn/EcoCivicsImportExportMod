using System;
using System.Windows.Input;

namespace EcoCivicsImportExportMod.Bundler
{
    public static class BundlerCommands
    {
		public static readonly RoutedUICommand AddToBundle = new RoutedUICommand
			(
				"Add to Bundle",
				"AddToBundle",
				typeof(BundlerCommands)
			);

		public static readonly RoutedUICommand RemoveFromBundle = new RoutedUICommand
			(
				"Remove from Bundle",
				"RemoveFromBundle",
				typeof(BundlerCommands)
			);
	}
}
