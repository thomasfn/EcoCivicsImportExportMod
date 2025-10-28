namespace Eco.Mods.CivicsImpExp
{
    using Core.Plugins.Interfaces;

    public class CivicsImpExpMod : IModInit
    {
        public static ModRegistration Register() => new()
        {
            ModName = "CivicsImportExport",
            ModDescription = "Adds admin-only commands that allow exporting and importing civics across worlds.",
            ModDisplayName = "Civics Import Export",
        };
    }
}
