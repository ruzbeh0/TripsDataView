using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.Internal;
using Game.Modding;
using Game.Settings;
using Game.UI.InGame;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;
using System.IO;

namespace TripsDataView
{
    [FileLocation(nameof(TripsDataView))]
    [SettingsUIGroupOrder(SettingsGroup)]
    [SettingsUIShowGroupName(SettingsGroup)]
    public class Setting : ModSetting
    {
        public const string SettingsSection = "Settings";

        public const string SettingsGroup = "Settings";

        public Setting(IMod mod) : base(mod)
        {

        }

        public override void SetDefaults()
        {


        }

        [SettingsUISection(SettingsSection, SettingsGroup)]
        [SettingsUIMultilineText]
        public string MultilineText => string.Empty;

        [SettingsUIDropdown(typeof(Setting), nameof(GetIntDropdownItems))]
        [SettingsUISection(SettingsSection, SettingsGroup)]
        public int numOutputs { get; set; } = 3;

        public DropdownItem<int>[] GetIntDropdownItems()
        {
            var items = new List<DropdownItem<int>>();

            for (var i = 1; i < 11; i += 1)
            {
                items.Add(new DropdownItem<int>()
                {
                    value = i,
                    displayName = i.ToString(),
                });
            }

            return items.ToArray();
        }


    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Trips View" },
                { m_Setting.GetOptionTabLocaleID(Setting.SettingsSection), "Settings" },

                { m_Setting.GetOptionGroupLocaleID(Setting.SettingsGroup), "Settings" },
                
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MultilineText)), $"Output Folder:\n{Mod.outputPath}" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.numOutputs)), "Number of output files to keep saved" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.numOutputs)), $"Number of output files to keep saved" },
            };
        }

        public void Unload()
        {

        }
    }
}
