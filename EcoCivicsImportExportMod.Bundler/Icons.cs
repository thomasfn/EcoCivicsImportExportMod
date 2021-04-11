using System;
using System.Collections.Generic;

namespace EcoCivicsImportExportMod.Bundler
{
    public static class Icons
    {
        private static readonly IReadOnlyDictionary<string, string> typesToIconSources = new Dictionary<string, string>
        {
            { EcoTypes.ConstitutionalAmendment, "/EcoCivicsImportExportMod.Bundler;component/Icons/paper-bag--plus.png" },
            { EcoTypes.Constitution, "/EcoCivicsImportExportMod.Bundler;component/Icons/paper-bag.png" },
            { EcoTypes.Demographic, "/EcoCivicsImportExportMod.Bundler;component/Icons/user-silhouette.png" },
            { EcoTypes.District, "/EcoCivicsImportExportMod.Bundler;component/Icons/map.png" },
            { EcoTypes.DistrictMap, "/EcoCivicsImportExportMod.Bundler;component/Icons/globe.png" },
            { EcoTypes.ElectedTitle, "/EcoCivicsImportExportMod.Bundler;component/Icons/user-business-boss.png" },
            { EcoTypes.ElectionProcess, "/EcoCivicsImportExportMod.Bundler;component/Icons/clipboard-list.png" },
            { EcoTypes.Law, "/EcoCivicsImportExportMod.Bundler;component/Icons/auction-hammer.png" },

            //{ EcoTypes.AppointedTitle, "/EcoCivicsImportExportMod.Bundler;component/Icons/auction-hammer.png" },
            { EcoTypes.Currency, "/EcoCivicsImportExportMod.Bundler;component/Icons/money.png" },
            { EcoTypes.PersonalAccount, "/EcoCivicsImportExportMod.Bundler;component/Icons/piggy-bank.png" },
            { EcoTypes.GovernmentAccount, "/EcoCivicsImportExportMod.Bundler;component/Icons/money-bag.png" },
            { EcoTypes.User, "/EcoCivicsImportExportMod.Bundler;component/Icons/user.png" }
        };

        private const string unknownTypeIcon = "/EcoCivicsImportExportMod.Bundler;component/Icons/question.png";

        public static string TypeToIconSource(string type)
            => typesToIconSources.TryGetValue(type, out var result) ? result : unknownTypeIcon;
    }
}
