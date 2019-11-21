using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Doccer_Bot.Datasets
{
    public class MarketOrderGearsetDataset
    {
        public static List<string> fendingSet = new List<string>()
        {
            "Gust Tongue",
            "Sinfender",
            "Dominus Shield",
            "Alastor",
            "Tutelary",
            "Facet Circlet of Fending",
            "Facet Mail of Fending",
            "Facet Gauntlets of Fending",
            "Facet Plate Belt of Fending",
            "Facet Chain Hose of Fending",
            "Facet Sabatons of Fending",
            "Facet Earrings of Fending",
            "Facet Choker of Fending",
            "Facet Bracelet of Fending",
            "Facet Ring of Fending"
        };

        public static List<string> healingSet = new List<string>()
        {
            "Clearpath",
            "Pragmatism",
            "Pollux",
            "Facet Hat of Healing",
            "Facet Coat of Healing",
            "Facet Dress Gloves of Healing",
            "Facet Plate Belt of Healing",
            "Facet Trousers of Healing",
            "Facet Boots of Healing",
            "Facet Earrings of Healing",
            "Facet Choker of Healing",
            "Facet Bracelet of Healing",
            "Facet Ring of Healing"
        };

        public static List<string> strikingSet = new List<string>()
        {
            "Burattinaios",
            "Sankhara",
            "Facet Circlet of Striking",
            "Facet Cyclas of Striking",
            "Facet Gloves of Striking",
            "Facet Plate Belt of Striking",
            "Facet Gaskins of Striking",
            "Facet Sandals of Striking",
            "Facet Bracelet of Slaying",
            "Facet Earrings of Slaying",
            "Facet Choker of Slaying",
            "Facet Ring of Slaying"
        };

        public static List<string> maimingSet = new List<string>()
        {
            "Skystrider",
            "Facet Circlet of Maiming",
            "Facet Mail of Maiming",
            "Facet Gauntlets of Maiming",
            "Facet Plate Belt of Maiming",
            "Facet Bottoms of Maiming",
            "Facet Sabatons of Maiming",
            "Facet Earrings of Slaying",
            "Facet Choker of Slaying",
            "Facet Bracelet of Slaying",
            "Facet Ring of Slaying",
        };

        public static List<string> scoutingSet = new List<string>()
        {
            "Silktones",
            "Facet Turban of Scouting",
            "Facet Dolman of Scouting",
            "Facet Gloves of Scouting",
            "Facet Plate Belt of Scouting",
            "Facet Bottoms of Scouting",
            "Facet Thighboots of Scouting",
            "Facet Earrings of Aiming",
            "Facet Choker of Aiming",
            "Facet Bracelet of Aiming",
            "Facet Ring of Aiming",
        };

        public static List<string> aimingSet = new List<string>()
        {
            "Barathrum",
            "Murderer",
            "Gendawa",
            "Facet Turban of Aiming",
            "Facet Tabard of Aiming",
            "Facet Halfgloves of Aiming",
            "Facet Plate Belt of Aiming",
            "Facet Brais of Aiming",
            "Facet Boots of Aiming",
            "Facet Earrings of Aiming",
            "Facet Choker of Aiming",
            "Facet Bracelet of Aiming",
            "Facet Ring of Aiming",
        };

        public static List<string> castingSet = new List<string>()
        {
            "Galdrabok",
            "Catalyst",
            "Merveilleuse",
            "Facet Hat of Casting",
            "Facet Coat of Casting",
            "Facet Halfqloves of Casting",
            "Facet Plate Belt of Casting",
            "Facet Bottoms of Casting",
            "Facet Boots of Casting",
            "Facet Earrings of Casting",
            "Facet Choker of Casting",
            "Facet Bracelet of Casting",
            "Facet Ring of Casting",
        };

        public static Dictionary<string, List<string>> Gearsets = new Dictionary<string, List<string>>()
        {
            { "fending", fendingSet },
            { "healing", healingSet },
            { "striking", strikingSet },
            { "maiming", maimingSet },
            { "scouting", scoutingSet },
            { "aiming", aimingSet },
            { "casting", castingSet },
        };
    }
}
