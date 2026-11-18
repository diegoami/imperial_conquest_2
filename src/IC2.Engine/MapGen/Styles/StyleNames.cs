namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294d: the authored generic name banks for the five fictional styles. Every name here is
/// invented for this project; no historical power is transplanted onto fictional geography
/// (docs/tasks/T294.md, owner decision 2026-11-09). A name may still echo a real place by
/// coincidence of common syllables — the guard test pins the explicit powers, not every syllable.
/// </summary>
public static class StyleNames
{
    public static readonly NameBank Mediterranean = new(
        Nations:
        [
            "Meridia", "Corvana", "Thalassia", "Ostria", "Seloria", "Varencia", "Calissa", "Norvica",
            "Ithara", "Pelagia", "Torvia", "Essaria", "Ligessa", "Arvenia", "Casuria", "Meloria",
            "Ophira", "Tesseria", "Brevia", "Haleria",
        ],
        Leaders:
        [
            "Athar", "Belor", "Cador", "Darion", "Emun", "Feras", "Gilar", "Haron", "Joran",
            "Kaleth", "Lumor", "Miron", "Noran", "Olfar", "Peran", "Quirin", "Rastan", "Sedor",
            "Tavor", "Ulmar", "Voran", "Yoran", "Zarel", "Ondar",
        ],
        Cities:
        [
            "Aladia", "Barvona", "Coranta", "Delinda", "Farento", "Galvena", "Marovia", "Norena",
            "Pelanto", "Torellia", "Valara", "Serasto", "Calvinda", "Ostoria", "Venara", "Tarura",
            "Mesena", "Ralissa", "Almeto", "Coruno", "Delossa", "Farissa", "Galonta", "Marnia",
        ],
        CityPrefixes:
        ["Al", "Bar", "Cor", "Del", "Far", "Gal", "Mar", "Nor", "Pel", "Tor", "Val", "Ser", "Cal", "Ost", "Ven", "Tar", "Mes", "Ral"],
        CitySuffixes:
        ["adia", "essa", "oria", "inda", "ona", "ara", "ium", "ena", "issa", "anto", "ura", "elia", "asto", "ovia"]);

    public static readonly NameBank MiddleEast = new(
        Nations:
        [
            "Suhmar", "Qadirra", "Bakhra", "Nawria", "Tashmin", "Harzan", "Yildara", "Karwan",
            "Zafra", "Mizran", "Alwana", "Sarvand", "Dihistan", "Ravandi", "Ghavand", "Ormara",
            "Sulandra", "Farsana", "Tavanda", "Sirmara",
        ],
        Leaders:
        [
            "Ardan", "Bahir", "Cihan", "Davan", "Emren", "Farud", "Givash", "Hordan", "Iltan",
            "Jamshad", "Kavir", "Loran", "Mehran", "Naveen", "Omid", "Parsal", "Rustel",
            "Shirvan", "Tavir", "Yamin", "Zorrin", "Kambiz", "Adnan", "Soran",
        ],
        Cities:
        [
            "Alqamin", "Bahrun", "Darshan", "Harraka", "Karmina", "Mahruz", "Naswad", "Qalbira",
            "Samkan", "Shahrun", "Tirwad", "Zahrin", "Kusbaha", "Ferwad", "Almasa", "Barudah",
            "Darmuna", "Hasqar", "Mawdan", "Qasrina", "Sahmun", "Shaqara", "Tirmun", "Zawana",
        ],
        CityPrefixes:
        ["Al", "Ba", "Dar", "Ha", "Kar", "Ma", "Nas", "Qal", "Sa", "Sha", "Tir", "Za", "Ku", "Fer", "Gha", "Mir"],
        CitySuffixes:
        ["bad", "min", "rah", "stan", "ura", "wah", "zir", "ara", "mun", "dis", "qar", "van", "shar", "rud", "zah", "nad"]);

    public static readonly NameBank CentralAsia = new(
        Nations:
        [
            "Karchal", "Tengir", "Ortagai", "Beshkul", "Saratal", "Uzunda", "Kalmir", "Argalyn",
            "Dzhamir", "Tepstan", "Urmand", "Zharken", "Ishtyk", "Kobdan", "Lapkhan", "Mirash",
            "Azhgar", "Kelteber", "Kortave", "Balghir",
        ],
        Leaders:
        [
            "Altan", "Batur", "Chogan", "Dorji", "Ergal", "Temir", "Gilgar", "Khangal", "Orman",
            "Pervan", "Saryan", "Togan", "Uzgal", "Yanchar", "Zheren", "Ardzhu", "Kartan",
            "Selbek", "Tural", "Ombar", "Berkut", "Chagel", "Tarhan", "Uzun",
        ],
        Cities:
        [
            "Karkent", "Ortagul", "Beshtal", "Sartash", "Uzkul", "Khargon", "Dzhalyn", "Tepgan",
            "Zharkul", "Ishtan", "Kobdan", "Laptal", "Mirgan", "Azkul", "Keltesh", "Argal",
            "Beshgan", "Ormtal", "Sarken", "Temirtal", "Uzgan", "Zhaltyk", "Kalmat", "Tepkul",
        ],
        CityPrefixes:
        ["Ka", "Or", "Bes", "Sar", "Uz", "Kha", "Dzha", "Te", "Zh", "Ish", "Ko", "La", "Mi", "Az", "Ber", "Tar"],
        CitySuffixes:
        ["kent", "kul", "tash", "gai", "tal", "anda", "mir", "gan", "stan", "lyk", "dan", "ken", "tyk", "han", "bek", "gat"]);

    public static readonly NameBank India = new(
        Nations:
        [
            "Avantika", "Malendra", "Tamarasa", "Kalyira", "Dandara", "Urjana", "Parnika",
            "Sindhura", "Vatsala", "Nehara", "Talajara", "Ambira", "Kuntala", "Rohanta",
            "Mahisha", "Trapura", "Ujjara", "Kaverata", "Vendhara", "Kolvara",
        ],
        Leaders:
        [
            "Rishan", "Vimal", "Antrik", "Ekaraj", "Jayadr", "Kirtan", "Manjun", "Praval",
            "Rajata", "Suran", "Taruns", "Udayar", "Vasvan", "Yaudha", "Indran", "Chandrav",
            "Harivan", "Somdutt", "Bhargav", "Devraj", "Kanvar", "Nagendra", "Prithvan", "Sarvajit",
        ],
        Cities:
        [
            "Avantara", "Malgiri", "Tamala", "Kalyira", "Danduka", "Urpali", "Parniket", "Sindpali",
            "Vatsara", "Nehalpur", "Taljara", "Ambikot", "Kuntrap", "Rohvada", "Mahishal", "Traveni",
            "Ujvala", "Kaverta", "Malpet", "Tamasin", "Vellara", "Dhanjara", "Parnala", "Sindhupat",
        ],
        CityPrefixes:
        ["Ava", "Mal", "Tam", "Kal", "Dan", "Ur", "Par", "Sin", "Vat", "Neh", "Tal", "Amb", "Kun", "Roh", "Tra", "Uj", "Ven", "Kol"],
        CitySuffixes:
        ["antika", "endra", "arasa", "yira", "dara", "jana", "ika", "dhura", "sala", "naha", "jara", "ira", "tala", "nta", "pura", "jjara", "ndhara", "vara"]);

    public static readonly NameBank China = new(
        Nations:
        [
            "Beilan", "Changwu", "Donglin", "Huaizhou", "Linrao", "Minyan", "Ningtai", "Qianzhou",
            "Ruizhou", "Tailing", "Wuzhan", "Yunqi", "Zhaoyang", "Anxuan", "Beiheng", "Cangming",
            "Shuolai", "Tieyun", "Taoyun", "Ganwei",
        ],
        Leaders:
        [
            "Liangyu", "Hongzhan", "Shiuan", "Wenzao", "Kangze", "Yuanlang", "Zhaoming", "Fuyan",
            "Guanhao", "Jingru", "Shenmu", "Taizhong", "Xiangyu", "Yonglin", "Zhiran", "Baoshan",
            "Chengen", "Daowu", "Yuanqi", "Haoshan", "Junzhe", "Mingyuan", "Ruoshan", "Tinglan",
        ],
        Cities:
        [
            "Jinhuan", "Changrao", "Luomei", "Guiluan", "Qingtai", "Pingrao", "Yanze", "Futai",
            "Haipu", "Ningche", "Tengshan", "Xinrao", "Anrao", "Baomei", "Jingsha", "Chenzhen",
            "Luokang", "Guiwan", "Qingma", "Pingtai", "Yanwu", "Fuzhen", "Hairao", "Ningle",
        ],
        CityPrefixes:
        ["Jin", "Chang", "Luo", "Gui", "Qing", "Ping", "Yan", "Fu", "Hai", "Ning", "Teng", "Xin", "An", "Bao", "Jing", "Chen"],
        CitySuffixes:
        ["shan", "yang", "zhou", "rao", "tai", "ling", "qi", "an", "jiang", "cheng", "pu", "xi", "huan", "mei", "wan", "ze"]);
}
