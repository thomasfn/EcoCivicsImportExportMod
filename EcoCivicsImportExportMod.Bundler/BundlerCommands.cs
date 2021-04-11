using System;
using System.Windows.Input;

namespace EcoCivicsImportExportMod.Bundler
{
    public static class BundlerCommands
    {
		public static readonly RoutedUICommand RemoveFromBundle = new RoutedUICommand
			(
				"Remove From Bundle",
				"RemoveFromBundle",
				typeof(BundlerCommands)
			);
	}
}
