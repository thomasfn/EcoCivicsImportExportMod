using System;

namespace Eco.Mods.CivicsImpExp
{
    using Shared.Localization;
    using Shared.Utils;

    public static class Logger
    {
        public static void Debug(string message)
        {
            Log.Write(new LocString("[CivicsImpExpPlugin] DEBUG: " + message + "\n"));
        }

        public static void Info(string message)
        {
            Log.Write(new LocString("[CivicsImpExpPlugin] " + message + "\n"));
        }

        public static void Error(string message)
        {
            Log.Write(new LocString("[CivicsImpExpPlugin] ERROR: " + message + "\n"));
        }
    }
}