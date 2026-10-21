#!/usr/bin/env python3
"""T202: builds the Italy 500 BC campaign from the tables below.

Writes tools/Italy/italy-500bc.png (the terrain source, in tools/TerrainImport's palette),
data/worlds/italy-500bc.json, data/rulesets/italy-500bc.json and data/scenarios/italy-500bc.json.
Then run the importer to write the terrain sidecar:

    dotnet run --project tools/TerrainImport -- import tools/Italy/italy-500bc.png --world data/worlds/italy-500bc.json

Deterministic: the same tables give byte-identical files. Owner decisions (2026-10-21): 500 -> 460 BC
(a 40-year reign), 12 nations all playable, Italy with Sicily, Sardinia and Corsica on a portrait map,
barbarians later (the Gauls are ordinary nations). No original-game data is used: the geography is
public, the history is cited in each entry's _provenance, and every game number is [designed].
"""
import json
import math
import struct
import zlib
from collections import deque
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORLD_ID = "italy-500bc"

# ---- the grid: an equirectangular frame, 12 tiles per degree of longitude, 16 per degree of latitude,
# which makes tiles about 6.9 km square at 42 N (cos 42 = 0.743; 111 km x 0.743 / 12 ~ 111 km / 16).
LON_MIN, LON_MAX, LAT_MIN, LAT_MAX = 6.5, 19.0, 36.5, 47.0
TPD_LON, TPD_LAT = 12, 16
WIDTH = round((LON_MAX - LON_MIN) * TPD_LON)   # 150
HEIGHT = round((LAT_MAX - LAT_MIN) * TPD_LAT)  # 168

GEOGRAPHY = ("historical: coastline, relief and rivers simplified from present-day geography "
             "(the modern coast is close enough at this scale; the Po delta and the Tuscan and Latin "
             "coastal plains have moved by a few kilometres since antiquity, under one tile here). "
             "designed: the simplification itself, drawn by hand as the polygons and polylines in "
             "tools/Italy/build_italy.py.")

def cell(lon, lat):
    return (int((lon - LON_MIN) * TPD_LON), int((LAT_MAX - lat) * TPD_LAT))

def centre(x, y):
    return (LON_MIN + (x + 0.5) / TPD_LON, LAT_MAX - (y + 0.5) / TPD_LAT)

# Land outlines as (lon, lat), clockwise or not. The mainland polygon follows the coast from the
# French Riviera round the peninsula and up the Adriatic to the Dalmatian coast, then closes along
# the frame's east, north and west edges.
MAINLAND = [
    (6.5, 43.15), (6.64, 43.27), (7.01, 43.55), (7.27, 43.70), (7.78, 43.82), (8.03, 43.88),
    (8.48, 44.31), (8.93, 44.41), (9.40, 44.27), (9.82, 44.10), (10.25, 43.87), (10.31, 43.55),
    (10.50, 42.93), (10.98, 42.70), (11.15, 42.40), (11.79, 42.09), (12.28, 41.73), (12.62, 41.45),
    (13.05, 41.22), (13.57, 41.21), (13.93, 41.02), (14.05, 40.85), (14.25, 40.84), (14.37, 40.63),
    (14.77, 40.68), (15.00, 40.42), (15.27, 40.03), (15.63, 40.07), (15.79, 39.81), (16.04, 39.36),
    (16.10, 38.95), (15.83, 38.62), (15.72, 38.25), (15.65, 38.11), (16.06, 37.92), (16.26, 38.24),
    (16.60, 38.45), (17.10, 38.90), (17.13, 39.08), (16.90, 39.45), (16.50, 39.75), (16.68, 40.20),
    (17.24, 40.47), (17.98, 40.06), (18.36, 39.80), (18.49, 40.15), (17.94, 40.64), (16.87, 41.12),
    (16.28, 41.32), (15.92, 41.63), (16.18, 41.88), (15.88, 41.93), (14.99, 42.00), (14.71, 42.12),
    (14.21, 42.46), (13.97, 42.75), (13.88, 42.95), (13.52, 43.62), (13.22, 43.72), (12.91, 43.91),
    (12.57, 44.06), (12.28, 44.42), (12.50, 44.95), (12.28, 45.22), (12.33, 45.44), (12.89, 45.60),
    (13.38, 45.68), (13.77, 45.65), (13.73, 45.55), (13.60, 45.10), (13.85, 44.87), (14.10, 45.20),
    (14.44, 45.33), (14.90, 44.99), (15.23, 44.12), (15.90, 43.73), (16.44, 43.51), (17.02, 43.30),
    (17.60, 43.00), (18.09, 42.65), (18.53, 42.45), (18.84, 42.29), (19.0, 42.18),
    (19.0, 47.0), (6.5, 47.0),
]
SICILY = [
    (15.55, 38.19), (15.65, 38.27), (15.24, 38.22), (14.60, 38.08), (14.02, 38.04), (13.36, 38.12),
    (13.00, 38.20), (12.73, 38.18), (12.50, 38.02), (12.43, 37.80), (12.59, 37.65), (13.08, 37.51),
    (13.58, 37.27), (13.94, 37.10), (14.25, 37.07), (14.85, 36.73), (15.14, 36.69), (15.10, 36.95),
    (15.29, 37.07), (15.22, 37.23), (15.09, 37.50), (15.29, 37.85),
]
SARDINIA = [
    (9.15, 41.24), (9.50, 40.92), (9.82, 40.53), (9.71, 39.94), (9.52, 39.10), (9.11, 39.21),
    (8.84, 38.88), (8.40, 39.05), (8.50, 39.90), (8.48, 40.30), (8.31, 40.56), (8.16, 40.56),
    (8.22, 40.94), (8.71, 40.91),
]
CORSICA = [
    (9.42, 43.01), (9.45, 42.70), (9.51, 42.10), (9.28, 41.59), (9.16, 41.39), (8.90, 41.68),
    (8.74, 41.92), (8.60, 42.13), (8.76, 42.57), (9.30, 42.68), (9.35, 42.95),
]
ELBA = [(10.10, 42.82), (10.43, 42.82), (10.43, 42.72), (10.10, 42.72)]
AFRICA = [  # the corner of north Africa the frame catches: land, but no city (owner decision D5)
    (6.5, 36.5), (6.5, 37.00), (7.40, 37.08), (7.77, 36.95), (8.76, 36.95), (9.22, 37.23),
    (9.87, 37.27), (10.27, 37.17), (10.30, 36.85), (10.60, 36.95), (11.05, 37.08), (11.10, 36.85),
    (10.90, 36.5),
]
LAND = [MAINLAND, SICILY, SARDINIA, CORSICA, ELBA, AFRICA]

# The Alpine and Dinaric mountain belt, as one polygon over the mainland's north and east.
ALPS = [
    (6.5, 43.85), (7.30, 44.10), (7.55, 44.45), (7.35, 44.75), (7.40, 45.20), (7.75, 45.50),
    (8.30, 45.80), (8.80, 45.95), (9.40, 45.85), (10.20, 45.90), (10.70, 45.85), (11.20, 46.00),
    (11.90, 46.00), (12.40, 46.15), (13.10, 46.20), (13.60, 46.00), (14.00, 45.75), (14.30, 45.60),
    (15.00, 45.30), (15.60, 44.70), (16.00, 44.30), (17.00, 43.60), (18.20, 43.00), (19.0, 42.60),
    (19.0, 47.0), (6.5, 47.0),
]
# Mountain spines as (polyline, half-width in degrees).
SPINES = [
    ([(7.60, 44.20), (8.30, 44.30), (8.90, 44.50), (9.50, 44.45), (10.00, 44.30)], 0.14),        # Ligurian Apennines
    ([(10.00, 44.30), (11.00, 44.10), (12.00, 43.70), (12.80, 43.20)], 0.18),                     # northern Apennines
    ([(12.80, 43.20), (13.30, 42.70), (13.56, 42.47), (13.90, 42.10), (14.50, 41.60)], 0.30),     # central Apennines
    ([(14.50, 41.60), (15.20, 41.00), (15.70, 40.50), (16.00, 40.00), (16.20, 39.90)], 0.20),     # southern Apennines
    ([(16.20, 39.90), (16.30, 39.30), (16.10, 38.50), (15.90, 38.20)], 0.14),                     # Calabria
    ([(13.40, 37.85), (14.00, 37.90), (14.80, 37.95)], 0.12),                                     # Madonie and Nebrodi
    ([(14.99, 37.75), (14.99, 37.75)], 0.12),                                                     # Etna
    ([(9.10, 40.40), (9.35, 39.95)], 0.22),                                                       # Gennargentu
    ([(8.95, 42.50), (9.10, 42.00), (9.20, 41.70)], 0.15),                                        # Corsica
]
FORESTS = [  # (lon, lat, radius in degrees)
    (12.15, 42.40, 0.13),   # the Ciminian forest (Livy 9.36)
    (15.95, 41.80, 0.15),   # the Gargano
    (16.50, 39.30, 0.20),   # the Sila
    (11.60, 44.75, 0.25),   # the Po lowland woods
    (9.40, 45.20, 0.20),
    (8.90, 40.10, 0.15),
]
RIVERS = {  # source to mouth; each must end in the sea
    "Padus (Po)": [(7.10, 44.70), (7.68, 45.07), (7.90, 45.19), (8.45, 45.13), (9.15, 45.12), (9.69, 45.05),
                   (10.02, 45.13), (10.80, 45.05), (11.13, 45.07), (11.60, 44.95), (12.20, 44.95), (12.60, 44.95)],
    "Tiberis (Tiber)": [(12.05, 43.78), (12.24, 43.46), (12.40, 43.10), (12.39, 42.46), (12.50, 42.10),
                        (12.48, 41.90), (12.25, 41.74), (12.15, 41.70)],
    "Arnus (Arno)": [(11.70, 43.80), (11.25, 43.77), (10.80, 43.70), (10.40, 43.70), (10.10, 43.68)],
    "Athesis (Adige)": [(11.00, 46.30), (11.00, 45.80), (10.99, 45.44), (11.50, 45.25), (11.90, 45.20), (12.40, 45.17)],
    "Volturnus (Volturno)": [(14.30, 41.60), (14.40, 41.30), (14.25, 41.08), (14.05, 41.03), (13.80, 41.00)],
    "Aufidus (Ofanto)": [(15.20, 40.85), (15.60, 41.05), (16.00, 41.25), (16.25, 41.45)],
}

# ---- nations: id, name, colour, glyph colour, capital, leader, war-likeness for the seat personality
HIST = "historical:"
NATIONS = [
    dict(id="rome", name="Rome", color="#B71C1C", glyph="#FFD54F", capital="roma",
         leader="Servius Sulpicius", treasury=900, unity=760,
         src=f"{HIST} the early Roman Republic and its Latin neighbours, founded (by tradition) in 509 BC (Livy 2.1); "
             "Servius Sulpicius Camerinus was consul in 500 BC (Livy 2.19)."),
    dict(id="etruria", name="Etruscans", color="#6A1B9A", glyph="#FFFFFF", capital="tarquinii",
         leader="Lars Porsenna", treasury=1600, unity=700,
         src=f"{HIST} the Etruscan cities, at their greatest extent c. 500 BC, from Campania to the Po (Polybius 2.17; Livy 5.33); "
             "Lars Porsenna of Clusium attacked Rome c. 508 BC (Livy 2.9-2.14). designed: one nation for a league of independent cities."),
    dict(id="samnites", name="Samnites", color="#2E7D32", glyph="#FFFFFF", capital="bovianum",
         leader="Gavius Pontius", treasury=500, unity=820,
         src=f"{HIST} the Samnite peoples of the central southern Apennines (Livy 7.29; Strabo 5.4.12). "
             "designed: the leader's name, an Oscan name of the kind Livy 9.1 records, a century later."),
    dict(id="umbrians", name="Umbrians", color="#F9A825", glyph="#3E2723", capital="iguvium",
         leader="Vibius Trebonius", treasury=400, unity=720,
         src=f"{HIST} the Umbrians east of the Tiber (Herodotus 1.94; Pliny, Natural History 3.112). designed: the leader's name."),
    dict(id="veneti", name="Veneti", color="#0277BD", glyph="#FFFFFF", capital="ateste",
         leader="Ostiala Gallenia", treasury=500, unity=740,
         src=f"{HIST} the Veneti at the head of the Adriatic (Herodotus 1.196; Polybius 2.17; Livy 1.1). designed: the leader's name."),
    dict(id="ligurians", name="Ligurians", color="#5D4037", glyph="#FFFFFF", capital="genua",
         leader="Comberanus", treasury=350, unity=760,
         src=f"{HIST} the Ligurian peoples of the north-west coast and Apennines (Strabo 4.6.1-4.6.3). designed: the leader's name."),
    dict(id="senones", name="Senones", color="#E65100", glyph="#FFFFFF", capital="sena",
         leader="Brennus", treasury=300, unity=780,
         src=f"{HIST} the Senones, the Gauls of the Adriatic coast who sacked Rome under Brennus c. 390 BC (Livy 5.35-5.38; Polybius 2.17). "
             "designed: placed in Italy a century early, at the owner's choice (decision D4), to give the Adriatic a Gaulish power."),
    dict(id="insubres", name="Insubres", color="#00897B", glyph="#FFFFFF", capital="mediolanum",
         leader="Bellovesus", treasury=400, unity=780,
         src=f"{HIST} the Insubres, the Gauls of the upper Po who founded Mediolanum under Bellovesus (Livy 5.34, by tradition c. 600 BC). "
             "designed: Bellovesus as the leader in 500 BC."),
    dict(id="campanians", name="Campanians", color="#AD1457", glyph="#FFFFFF", capital="nuceria",
         leader="Marius Statius", treasury=700, unity=690,
         src=f"{HIST} the Oscan-speaking towns of Campania (Strabo 5.4.3-5.4.8). designed: one nation for them, and the leader's name."),
    dict(id="magnagraecia", name="Magna Graecia", color="#1565C0", glyph="#FFEB3B", capital="taras",
         leader="Aristophilides", treasury=1400, unity=650,
         src=f"{HIST} the Greek cities of southern Italy (Strabo 6.1-6.3); Aristophilides was king of Taras c. 520 BC (Herodotus 3.136). "
             "designed: one nation, led from Taras, for many independent poleis."),
    dict(id="syracuse", name="Syracuse", color="#4527A0", glyph="#FFFFFF", capital="syracusae",
         leader="Gelon", treasury=1500, unity=680,
         src=f"{HIST} the Greek cities of Sicily (Thucydides 6.3-6.5); Gelon, master of Gela from 491 BC and of Syracuse from 485 BC (Herodotus 7.154-7.156). "
             "designed: Syracuse leading the Sicilian Greeks already in 500 BC, and Gelon as its leader."),
    dict(id="carthage", name="Carthage", color="#37474F", glyph="#FFC107", capital="motya",
         leader="Hamilcar", treasury=2400, unity=700,
         src=f"{HIST} Carthage's holdings in western Sicily and Sardinia (Thucydides 6.2.6; Polybius 3.22, the first treaty with Rome, 509 BC); "
             "Hamilcar commanded at Himera in 480 BC (Herodotus 7.165-7.167). designed: Africa is left out (decision D5), so the capital is Motya."),
]

# ---- cities: id, name, lon, lat, owner, population (thousands), fortification, source
def C(id, name, lon, lat, owner, pop, fort, src):
    return dict(id=id, name=name, lon=lon, lat=lat, owner=owner, pop=pop, fort=fort, src=src)

LOC = "site location from the Barrington Atlas of the Greek and Roman World (ed. R. Talbert, 2000), rounded to the map grid"
CITIES = [
    # Rome and Latium
    C("roma", "Roma", 12.48, 41.89, "rome", 30, 50, "Rome (Livy 1-2)."),
    C("tibur", "Tibur", 12.80, 41.96, "rome", 8, 35, "a Latin town (Livy 7.9)."),
    C("praeneste", "Praeneste", 12.89, 41.84, "rome", 9, 40, "a Latin town (Livy 3.8)."),
    C("ardea", "Ardea", 12.54, 41.61, "rome", 7, 35, "the Rutulian town besieged by the last king (Livy 1.57)."),
    C("antium", "Antium", 12.63, 41.45, "rome", 8, 30, "the Volscian port (Livy 2.33). designed: held by Rome's side in 500 BC."),
    # Etruria
    C("tarquinii", "Tarquinii", 11.76, 42.25, "etruria", 22, 45, "an Etruscan city (Livy 1.34)."),
    C("veii", "Veii", 12.40, 42.02, "etruria", 20, 50, "the Etruscan city at war with Rome in the early Republic (Livy 2.48-2.53)."),
    C("caere", "Caere", 12.10, 42.00, "etruria", 18, 40, "an Etruscan city (Livy 1.2)."),
    C("vulci", "Vulci", 11.63, 42.42, "etruria", 14, 35, "an Etruscan city (Pliny, Natural History 3.52)."),
    C("volsinii", "Volsinii", 12.11, 42.72, "etruria", 15, 45, "an Etruscan city (Livy 5.31)."),
    C("clusium", "Clusium", 11.95, 43.02, "etruria", 16, 40, "Porsenna's city (Livy 2.9)."),
    C("perusia", "Perusia", 12.39, 43.11, "etruria", 12, 45, "an Etruscan city (Livy 9.37)."),
    C("arretium", "Arretium", 11.88, 43.46, "etruria", 12, 35, "an Etruscan city (Livy 9.37)."),
    C("volaterrae", "Volaterrae", 10.86, 43.40, "etruria", 12, 50, "an Etruscan city (Strabo 5.2.6)."),
    C("populonia", "Populonia", 10.49, 42.99, "etruria", 8, 30, "the Etruscan iron port facing Elba (Strabo 5.2.6)."),
    C("faesulae", "Faesulae", 11.29, 43.81, "etruria", 8, 35, "an Etruscan town (Polybius 2.25)."),
    C("felsina", "Felsina", 11.34, 44.49, "etruria", 12, 30, "Etruscan Bologna (Pliny, Natural History 3.115)."),
    C("spina", "Spina", 12.13, 44.70, "etruria", 10, 20, "the Etruscan port of the Po delta (Strabo 5.1.7)."),
    C("mantua", "Mantua", 10.79, 45.16, "etruria", 7, 25, "an Etruscan town of the Po (Pliny, Natural History 3.130)."),
    C("capua", "Capua", 14.25, 41.08, "etruria", 16, 40, "Etruscan Capua, taken by the Samnites in 423 BC (Livy 4.37)."),
    C("alalia", "Alalia", 9.51, 42.10, "etruria", 5, 25, "the Corsican port held by the Etruscans after the battle of Alalia, c. 540 BC (Herodotus 1.165-1.167)."),
    # Samnium
    C("bovianum", "Bovianum", 14.47, 41.49, "samnites", 10, 40, "a chief Samnite town (Livy 9.31)."),
    C("aesernia", "Aesernia", 14.23, 41.59, "samnites", 6, 35, "a Samnite town (Livy 10.31; Strabo 5.3.10)."),
    C("saepinum", "Saepinum", 14.62, 41.43, "samnites", 6, 35, "a Samnite town (Livy 10.45)."),
    C("caudium", "Caudium", 14.64, 41.06, "samnites", 6, 35, "the Samnite town by the Caudine Forks (Livy 9.2)."),
    C("maleventum", "Maleventum", 14.78, 41.13, "samnites", 8, 35, "the Samnite town later renamed Beneventum (Livy 9.27; Pliny, Natural History 3.105)."),
    C("aufidena", "Aufidena", 14.03, 41.73, "samnites", 5, 40, "a Samnite town (Livy 10.12)."),
    # Umbria
    C("iguvium", "Iguvium", 12.58, 43.35, "umbrians", 7, 40, "the Umbrian town of the Iguvine Tables."),
    C("tuder", "Tuder", 12.41, 42.78, "umbrians", 7, 40, "an Umbrian town (Pliny, Natural History 3.113)."),
    C("asisium", "Asisium", 12.62, 43.07, "umbrians", 5, 35, "an Umbrian town (Pliny, Natural History 3.113)."),
    C("spoletium", "Spoletium", 12.74, 42.73, "umbrians", 6, 40, "an Umbrian town (Livy 22.9)."),
    C("ameria", "Ameria", 12.42, 42.55, "umbrians", 5, 35, "an Umbrian town (Pliny, Natural History 3.114)."),
    C("sarsina", "Sarsina", 12.14, 43.92, "umbrians", 4, 30, "an Umbrian town (Polybius 2.24)."),
    # Veneti
    C("ateste", "Ateste", 11.66, 45.23, "veneti", 9, 30, "the chief Venetic centre of the early Iron Age (Este)."),
    C("patavium", "Patavium", 11.88, 45.41, "veneti", 11, 35, "the Venetic city (Livy 10.2; Strabo 5.1.7)."),
    C("altinum", "Altinum", 12.40, 45.54, "veneti", 6, 25, "a Venetic port (Strabo 5.1.7)."),
    C("vicetia", "Vicetia", 11.55, 45.55, "veneti", 5, 25, "a Venetic town (Strabo 5.1.8)."),
    C("opitergium", "Opitergium", 12.49, 45.78, "veneti", 5, 25, "a Venetic town (Strabo 5.1.8)."),
    # Liguria
    C("genua", "Genua", 8.93, 44.41, "ligurians", 7, 30, "the Ligurian port (Strabo 4.6.1)."),
    C("albingaunum", "Albingaunum", 8.22, 44.05, "ligurians", 4, 30, "a Ligurian town (Strabo 4.6.1)."),
    C("intimilium", "Album Intimilium", 7.61, 43.79, "ligurians", 4, 30, "a Ligurian town (Strabo 4.6.1)."),
    C("segesta-t", "Segesta Tigulliorum", 9.40, 44.27, "ligurians", 3, 25, "a Ligurian town (Pliny, Natural History 3.48)."),
    C("taurasia", "Taurasia", 7.68, 45.07, "ligurians", 5, 30, "the town of the Taurini (Appian, Hannibalic War 5; Polybius 3.60)."),
    # Senones
    C("sena", "Sena Gallica", 13.22, 43.71, "senones", 5, 25, "the Senones' coastal town (Polybius 2.19). designed: Gaulish a century early."),
    C("ariminum", "Ariminum", 12.57, 44.06, "senones", 6, 25, "the site of Ariminum (Polybius 2.21). designed: held by the Senones in 500 BC."),
    C("pisaurum", "Pisaurum", 12.91, 43.91, "senones", 4, 20, "the site of Pisaurum (Livy 39.44). designed: held by the Senones in 500 BC."),
    C("fanum", "Fanum", 13.02, 43.84, "senones", 3, 20, "the site of Fanum Fortunae. designed: a Senonian town in 500 BC."),
    # Insubres
    C("mediolanum", "Mediolanum", 9.19, 45.46, "insubres", 9, 30, "the Insubres' town (Livy 5.34; Polybius 2.34)."),
    C("comum", "Comum", 9.09, 45.81, "insubres", 5, 30, "a town of the lakes (Livy 33.36)."),
    C("novaria", "Novaria", 8.62, 45.45, "insubres", 4, 25, "a town of the upper Po plain (Pliny, Natural History 3.124)."),
    C("laus", "Laus", 9.42, 45.30, "insubres", 4, 25, "Laus Pompeia, an Insubrian town (Pliny, Natural History 3.124)."),
    C("bergomum", "Bergomum", 9.67, 45.69, "insubres", 5, 30, "a town of the Orobii (Pliny, Natural History 3.124). designed: Insubrian here."),
    C("brixia", "Brixia", 10.22, 45.54, "insubres", 5, 30, "the Cenomani's town (Livy 32.30). designed: Insubrian here."),
    # Campania
    C("nuceria", "Nuceria", 14.65, 40.74, "campanians", 9, 35, "Nuceria Alfaterna, an Oscan town (Livy 9.41)."),
    C("nola", "Nola", 14.53, 40.93, "campanians", 9, 40, "an Oscan-Campanian town (Livy 8.23)."),
    C("pompeii", "Pompeii", 14.49, 40.75, "campanians", 6, 30, "an Oscan town (Strabo 5.4.8)."),
    C("acerrae", "Acerrae", 14.37, 40.95, "campanians", 5, 25, "a Campanian town (Livy 8.17)."),
    C("abella", "Abella", 14.60, 40.98, "campanians", 4, 25, "a Campanian town (Virgil, Aeneid 7.740)."),
    # Magna Graecia
    C("taras", "Taras", 17.24, 40.47, "magnagraecia", 24, 50, "the Spartan colony (Strabo 6.3.2)."),
    C("metapontum", "Metapontum", 16.82, 40.38, "magnagraecia", 12, 35, "an Achaean colony (Strabo 6.1.15)."),
    C("croton", "Croton", 17.13, 39.08, "magnagraecia", 20, 40, "the Achaean colony that destroyed Sybaris in 510 BC (Strabo 6.1.12-6.1.13)."),
    C("locri", "Locri", 16.26, 38.24, "magnagraecia", 12, 35, "Locri Epizephyrii (Strabo 6.1.7)."),
    C("rhegium", "Rhegium", 15.65, 38.11, "magnagraecia", 12, 40, "the Chalcidian colony on the strait (Strabo 6.1.6)."),
    C("caulonia", "Caulonia", 16.45, 38.38, "magnagraecia", 6, 30, "an Achaean colony (Strabo 6.1.10)."),
    C("hipponion", "Hipponion", 16.10, 38.68, "magnagraecia", 6, 30, "a Locrian colony (Strabo 6.1.5)."),
    C("elea", "Elea", 15.15, 40.16, "magnagraecia", 6, 30, "the Phocaean colony founded c. 540 BC (Herodotus 1.167)."),
    C("poseidonia", "Poseidonia", 15.01, 40.42, "magnagraecia", 9, 35, "the Sybarite colony, later Paestum (Strabo 5.4.13)."),
    C("cumae", "Cumae", 14.05, 40.85, "magnagraecia", 12, 45, "the oldest Greek colony in Italy, under Aristodemus c. 504 BC (Dionysius of Halicarnassus 7.3-7.12)."),
    # Sicilian Greeks
    C("syracusae", "Syracusae", 15.29, 37.07, "syracuse", 25, 55, "the Corinthian colony (Thucydides 6.3)."),
    C("gela", "Gela", 14.25, 37.07, "syracuse", 14, 40, "the Rhodian-Cretan colony, Gelon's first city (Thucydides 6.4; Herodotus 7.153-7.156)."),
    C("akragas", "Akragas", 13.58, 37.31, "syracuse", 16, 45, "Gela's colony (Thucydides 6.4). designed: on Syracuse's side in 500 BC."),
    C("leontini", "Leontini", 15.00, 37.29, "syracuse", 8, 35, "a Chalcidian colony (Thucydides 6.3)."),
    C("catana", "Catana", 15.09, 37.50, "syracuse", 9, 35, "a Chalcidian colony (Thucydides 6.3)."),
    C("naxos", "Naxos", 15.27, 37.82, "syracuse", 5, 30, "the first Greek colony in Sicily (Thucydides 6.3)."),
    C("camarina", "Camarina", 14.45, 36.87, "syracuse", 6, 30, "a Syracusan colony (Thucydides 6.5)."),
    C("zancle", "Zancle", 15.55, 38.19, "syracuse", 8, 35, "the colony on the strait, later Messana (Thucydides 6.4)."),
    C("himera", "Himera", 13.82, 37.97, "syracuse", 7, 35, "Zancle's colony, the battlefield of 480 BC (Thucydides 6.5; Herodotus 7.165)."),
    C("selinus", "Selinus", 12.82, 37.58, "syracuse", 10, 40, "the Megarian colony (Thucydides 6.4). designed: on Syracuse's side."),
    # Carthage
    C("motya", "Motya", 12.47, 37.87, "carthage", 10, 45, "the Phoenician island town of western Sicily (Thucydides 6.2.6)."),
    C("panormus", "Panormus", 13.36, 38.12, "carthage", 12, 45, "a Phoenician town of Sicily (Thucydides 6.2.6)."),
    C("soluntum", "Soluntum", 13.53, 38.09, "carthage", 5, 35, "a Phoenician town of Sicily (Thucydides 6.2.6)."),
    C("eryx", "Eryx", 12.59, 38.04, "carthage", 5, 45, "the Elymian town allied to Carthage (Thucydides 6.2.3; Diodorus 14.48). designed: Carthaginian here."),
    C("segesta", "Segesta", 12.84, 37.94, "carthage", 6, 40, "the Elymian town allied to Carthage (Thucydides 6.2.3). designed: Carthaginian here."),
    C("karalis", "Karalis", 9.11, 39.22, "carthage", 8, 35, "the Phoenician-Punic town of Sardinia (Pausanias 10.17.9)."),
    C("nora", "Nora", 9.02, 38.98, "carthage", 5, 30, "a Phoenician town of Sardinia (Pausanias 10.17.5)."),
    C("tharros", "Tharros", 8.44, 39.87, "carthage", 6, 30, "a Phoenician-Punic town of Sardinia (Ptolemy 3.3.2)."),
    C("sulcis", "Sulcis", 8.46, 39.06, "carthage", 6, 30, "a Phoenician town of Sardinia (Pausanias 10.17.9)."),
    C("olbia", "Olbia", 9.50, 40.92, "carthage", 4, 25, "the port of north-east Sardinia (Pausanias 10.17.5). designed: Carthaginian here."),
]

WARS = [  # starting wars: (a, b, source)
    ("rome", "etruria", "historical: Rome's wars with Veii and Porsenna in the first years of the Republic (Livy 2.6-2.15, 2.42-2.53)."),
    ("magnagraecia", "etruria", "historical: Aristodemus of Cumae fought the Etruscans at Cumae (524 BC) and Aricia (c. 504 BC) (Dionysius of Halicarnassus 7.3-7.6; Livy 2.14)."),
    ("syracuse", "carthage", "historical: the Sicilian Greeks' wars with Carthage, which ended at Himera in 480 BC (Herodotus 7.165-7.167). designed: at war from the start."),
    ("samnites", "campanians", "designed: the Samnites' push into Campania, which took Capua in 423 BC (Livy 4.37), begun early."),
]

# ---- helpers -------------------------------------------------------------------------------------------
def inside(poly, lon, lat):
    hit = False
    j = len(poly) - 1
    for i in range(len(poly)):
        (xi, yi), (xj, yj) = poly[i], poly[j]
        if (yi > lat) != (yj > lat) and lon < (xj - xi) * (lat - yi) / (yj - yi) + xi:
            hit = not hit
        j = i
    return hit

def dist_to_segment(px, py, a, b):
    # in degrees, with longitude scaled to latitude-degree length at 42 N
    k = math.cos(math.radians(42))
    ax, ay, bx, by, px = a[0] * k, a[1], b[0] * k, b[1], px * k
    dx, dy = bx - ax, by - ay
    t = 0.0 if dx == dy == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))

def line_cells(a, b):
    """4-connected cells from a to b."""
    (x0, y0), (x1, y1) = a, b
    cells = [(x0, y0)]
    steps = max(abs(x1 - x0), abs(y1 - y0)) * 4 or 1
    for s in range(1, steps + 1):
        x = round(x0 + (x1 - x0) * s / steps)
        y = round(y0 + (y1 - y0) * s / steps)
        px, py = cells[-1]
        if (x, y) == (px, py):
            continue
        if x != px and y != py:
            cells.append((x, py))
        cells.append((x, y))
    return cells

EXIT_CODE = {frozenset("WE"): 6, frozenset("NS"): 7, frozenset("EN"): 8, frozenset("ES"): 9, frozenset("WS"): 10, frozenset("WN"): 11}
def direction(a, b):
    return {(1, 0): "E", (-1, 0): "W", (0, 1): "S", (0, -1): "N"}[(b[0] - a[0], b[1] - a[1])]
OPPOSITE = {"E": "W", "W": "E", "N": "S", "S": "N"}

# ---- terrain -------------------------------------------------------------------------------------------
def build_terrain():
    grid = [[0] * WIDTH for _ in range(HEIGHT)]
    for y in range(HEIGHT):
        for x in range(WIDTH):
            lon, lat = centre(x, y)
            if not any(inside(p, lon, lat) for p in LAND):
                grid[y][x] = 0
                continue
            code = 2
            if inside(ALPS, lon, lat) and inside(MAINLAND, lon, lat):
                code = 5
            for line, half in SPINES:
                for a, b in zip(line, line[1:]):
                    if dist_to_segment(lon, lat, a, b) <= half:
                        code = 5
            for flon, flat, r in FORESTS:
                if code == 2 and dist_to_segment(lon, lat, (flon, flat), (flon, flat)) <= r:
                    code = 4
            grid[y][x] = code
    # designed: open water at least ROUGH_SEA_DISTANCE tiles from any land is rough (the deep Tyrrhenian and Ionian)
    distance = [[None] * WIDTH for _ in range(HEIGHT)]
    queue = deque()
    for y in range(HEIGHT):
        for x in range(WIDTH):
            if grid[y][x] != 0:
                distance[y][x] = 0
                queue.append((x, y))
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < WIDTH and 0 <= ny < HEIGHT and distance[ny][nx] is None:
                distance[ny][nx] = distance[y][x] + 1
                queue.append((nx, ny))
    for y in range(HEIGHT):
        for x in range(WIDTH):
            if grid[y][x] == 0 and distance[y][x] >= ROUGH_SEA_DISTANCE:
                grid[y][x] = 1
    for name, points in RIVERS.items():
        path = []
        for a, b in zip(points, points[1:]):
            seg = line_cells(cell(*a), cell(*b))
            path.extend(seg if not path else seg[1:])
        # cut at the first sea cell: that is the mouth
        river = []
        mouth = None
        for c in path:
            if grid[c[1]][c[0]] in (0, 1):
                mouth = c
                break
            if river and abs(river[-1][0] - c[0]) + abs(river[-1][1] - c[1]) != 1:
                raise SystemExit(f"{name}: not 4-connected at {c}")
            if c not in river:
                river.append(c)
        if mouth is None:
            raise SystemExit(f"{name} never reaches the sea")
        for i, c in enumerate(river):
            nxt = river[i + 1] if i + 1 < len(river) else mouth
            exits = {direction(c, nxt)}
            exits.add(direction(c, river[i - 1]) if i > 0 else OPPOSITE[direction(c, nxt)])
            grid[c[1]][c[0]] = EXIT_CODE[frozenset(exits)]
    return grid

ROUGH_SEA_DISTANCE = 14

PALETTE = [0x2A5CAA, 0x14306A, 0x9CCB5A, 0xE8D58A, 0x2E7D32, 0x8D6E63,
           0x80DEEA, 0x4DD0E1, 0x26C6DA, 0x00BCD4, 0x00ACC1, 0x0097A7]  # tools/TerrainImport palette

def write_png(path, grid):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + bytes(row) for row in grid)
    plte = b"".join(struct.pack(">BBB", c >> 16, (c >> 8) & 255, c & 255) for c in PALETTE)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 3, 0, 0, 0))
           + chunk(b"PLTE", plte) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    path.write_bytes(png)

# ---- world ---------------------------------------------------------------------------------------------
def nearest(grid, start, ok, taken):
    seen = {start}
    queue = deque([start])
    while queue:
        x, y = queue.popleft()
        if ok(grid[y][x]) and (x, y) not in taken:
            return (x, y)
        for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= n[0] < WIDTH and 0 <= n[1] < HEIGHT and n not in seen:
                seen.add(n)
                queue.append(n)
    raise SystemExit(f"no cell near {start}")

LAND_CELL = lambda code: code in (2, 3, 4, 5)
SEA_CELL = lambda code: code in (0, 1)

UNIT_NAMES = {"light_infantry": "Foot", "heavy_infantry": "Spearmen", "archers": "Archers",
              "light_cavalry": "Horse", "heavy_cavalry": "Knights"}
ARMIES = {  # designed starting forces: (unit type, troops, quality)
    "rome": [("heavy_infantry", 4200, 7), ("light_infantry", 3000, 6), ("light_cavalry", 600, 5)],
    "etruria": [("heavy_infantry", 4500, 7), ("heavy_infantry", 3500, 6), ("archers", 1500, 5), ("heavy_cavalry", 800, 6)],
    "samnites": [("light_infantry", 4000, 7), ("heavy_infantry", 3000, 7), ("light_cavalry", 700, 6)],
    "umbrians": [("light_infantry", 3000, 5), ("heavy_infantry", 2000, 5)],
    "veneti": [("light_infantry", 2500, 5), ("light_cavalry", 1200, 6)],
    "ligurians": [("light_infantry", 3500, 6), ("archers", 800, 5)],
    "senones": [("light_infantry", 4000, 6), ("heavy_cavalry", 1200, 6)],
    "insubres": [("light_infantry", 4500, 6), ("heavy_cavalry", 1500, 6)],
    "campanians": [("heavy_infantry", 2500, 5), ("light_cavalry", 1200, 6)],
    "magnagraecia": [("heavy_infantry", 5000, 7), ("archers", 1200, 6), ("light_cavalry", 800, 6)],
    "syracuse": [("heavy_infantry", 5000, 7), ("archers", 1500, 6), ("heavy_cavalry", 1000, 7)],
    "carthage": [("heavy_infantry", 4000, 6), ("light_infantry", 2500, 6), ("light_cavalry", 1000, 6)],
}
FLEETS = {"etruria": ("caere", 40), "magnagraecia": ("taras", 30), "syracuse": ("syracusae", 50),
          "carthage": ("motya", 60), "campanians": ("pompeii", 15)}
PERSONALITY = {"rome": (0.6, 0.7), "etruria": (0.5, 0.5), "samnites": (0.8, 0.6), "umbrians": (0.3, 0.3),
               "veneti": (0.3, 0.3), "ligurians": (0.5, 0.4), "senones": (0.9, 0.8), "insubres": (0.8, 0.7),
               "campanians": (0.4, 0.4), "magnagraecia": (0.4, 0.5), "syracuse": (0.7, 0.7), "carthage": (0.6, 0.6)}
WEALTH_PER_THOUSAND = 3000

def build_world(grid):
    classical = json.loads((ROOT / "data/worlds/classical-mediterranean.json").read_text())
    taken = set()
    cities = []
    for c in CITIES:
        x, y = nearest(grid, cell(c["lon"], c["lat"]), LAND_CELL, taken)
        taken.add((x, y))
        tribute = max(1, round(c["pop"] * 0.7))
        cities.append(dict(
            id=c["id"], name=c["name"], x=x, y=y, owner=c["owner"], allegiance=c["owner"], loyalty=80,
            supplyTons=40 + c["pop"] * 6, fortificationCode=c["fort"], populationThousands=c["pop"],
            maxPopulationThousands=round(c["pop"] * 1.5), tribute=tribute, garrison=[],
            _provenance={
                "_group": f"{HIST} {c['name']}: {c['src']} {LOC}.",
                "owner": "designed: the owner in 500 BC, simplified to the campaign's twelve nations from the city's history above.",
                "loyalty": "designed: every city starts at 80, above the 65 defection floor.",
                "populationThousands": "designed: a relative size, larger for the cities the ancient sources treat as leading; no census exists for 500 BC.",
                "fortificationCode": "designed: larger for the cities the sources call strong; same scale as the classical world's codes.",
                "supplyTons": "designed: 40 tons plus 6 per thousand people.",
                "tribute": "designed: 0.7 per thousand people.",
                "maxPopulationThousands": "designed: half as much again as the starting population.",
            }))
    by_owner = {}
    for c in cities:
        by_owner.setdefault(c["owner"], []).append(c)
    nations, armies, fleets = [], [], []
    for n in NATIONS:
        own = by_owner[n["id"]]
        pop = sum(c["populationThousands"] for c in own)
        capital = next(c for c in own if c["id"] == n["capital"])
        nations.append(dict(
            id=n["id"], name=n["name"], colorHex=n["color"], leaderName=n["leader"], capitalCityId=n["capital"],
            treasury=n["treasury"], unity=n["unity"], wealth=pop * WEALTH_PER_THOUSAND,
            taxBase=4 * sum(c["tribute"] for c in own), taxRatePercent=10, mobilizedPercent=20, population=pop,
            battleColorsHex=[n["color"], "#FFFFFF", n["glyph"]], glyphColorHex=n["glyph"],
            _provenance={
                "_group": n["src"],
                "colorHex": "designed: a colour of its own, told apart from its neighbours on the map.",
                "treasury": "designed: larger for the trading powers (Carthage, the Greeks, Etruria).",
                "unity": "designed: higher for the hill peoples and the Gauls, lower for the league-like Greeks and Etruscans.",
                "population": "derived: the sum of its cities' populations.",
                "wealth": "derived: population x 3000, the classical world's own ratio.",
                "taxBase": "derived: four times its cities' tribute, the classical world's own ratio.",
                "taxRatePercent": "designed: 10%, the classical world's usual rate.",
                "mobilizedPercent": "designed: 20%.",
            }))
        ax, ay = nearest(grid, (capital["x"] + 1, capital["y"]), LAND_CELL, taken)
        taken.add((ax, ay))
        units = [dict(mercenaryLabel=0, unitTypeId=t, troops=troops, quality=q,
                      name=f"{i + 1}{['st', 'nd', 'rd', 'th'][min(i, 3)]} {UNIT_NAMES[t]}")
                 for i, (t, troops, q) in enumerate(ARMIES[n["id"]])]
        armies.append(dict(id=f"army-{n['id']}", nation=n["id"], x=ax, y=ay, morale=70, money=100,
                           supplyTons=120, moves=8, units=units,
                           _provenance={"_group": "designed: one field army beside the capital; sizes larger for the military powers the sources describe."}))
        if n["id"] in FLEETS:
            port_id, ships = FLEETS[n["id"]]
            port = next(c for c in own if c["id"] == port_id)
            fx, fy = nearest(grid, (port["x"], port["y"]), SEA_CELL, taken)
            taken.add((fx, fy))
            fleets.append(dict(id=f"fleet-{n['id']}", nation=n["id"], x=fx, y=fy, ships=ships, conditionPercent=90,
                               money=200, supplyTons=60, moves=25,
                               _provenance={"_group": "designed: a fleet off its main port, for the sea powers (Herodotus 1.166 on the Etruscan and Carthaginian fleets at Alalia; Thucydides 1.14 on Sicilian Greek fleets)."}))
    ids = [n["id"] for n in NATIONS]
    matrix = [[0] * len(ids) for _ in ids]
    for a, b, _ in WARS:
        i, j = ids.index(a), ids.index(b)
        matrix[i][j] = matrix[j][i] = 3
    weather = [nearest(grid, cell(lon, lat), SEA_CELL, set()) for lon, lat in [
        (10.8, 41.5), (11.8, 40.5), (12.8, 39.8), (13.8, 39.5), (11.0, 39.0), (12.0, 39.0), (14.5, 39.2),
        (10.5, 40.2), (11.4, 42.0), (9.9, 42.3), (8.0, 41.8), (7.6, 42.8), (8.3, 43.5), (17.5, 38.5),
        (17.0, 37.6), (16.2, 37.2), (17.6, 41.8), (16.0, 42.6), (14.6, 43.4), (13.4, 44.5)]]
    for wx, wy in weather:
        if min(wx, wy, WIDTH - 1 - wx, HEIGHT - 1 - wy) < 10:
            raise SystemExit(f"weather centre ({wx}, {wy}) is within 10 tiles of the edge")
    world = dict(
        schemaVersion=1, id=WORLD_ID, name="Italy, 500 BC", width=WIDTH, height=HEIGHT,
        terrain=dict(encoding="base64", runs=None, data=None, dataFile=f"{WORLD_ID}.terrain.b64"),
        tileTypes=classical["tileTypes"], nations=nations, cities=cities, startingArmies=armies,
        startingFleets=fleets, turnOrder=ids,
        startingRelations=dict(nationIds=ids, matrix=matrix),
        weatherCentres=[dict(x=x, y=y) for x, y in weather],
        _provenance={
            "terrain": GEOGRAPHY + " Built by tools/Italy/build_italy.py into tools/Italy/italy-500bc.png and imported with tools/TerrainImport.",
            "width": "designed: 12.5 degrees of longitude (6.5 E to 19 E) at 12 tiles per degree.",
            "height": "designed: 10.5 degrees of latitude (36.5 N to 47 N) at 16 tiles per degree, so tiles are about 6.9 km square.",
            "tileTypes": "designed: the twelve terrain types of the classical world, reused unchanged so the ruleset's costs apply.",
            "turnOrder": "designed: the nations in the order of NATIONS in tools/Italy/build_italy.py.",
            "startingRelations": "designed: peace everywhere except these wars. " + " ".join(f"{a}-{b}: {s}" for a, b, s in WARS),
            "weatherCentres": "designed: twenty points of open sea, each at least 10 tiles from the edge, for the rough-sea overlay.",
            "startingFleets": "designed: one fleet each for the sea powers.",
        })
    return world

def build_ruleset():
    ruleset = json.loads((ROOT / "data/rulesets/classical-faithful.json").read_text())
    ruleset["id"] = WORLD_ID
    ruleset["name"] = "Italy, 500 BC"
    ruleset["description"] = ("The classical-faithful rules, unchanged except for the calendar: the campaign "
                              "runs from 500 BC to 460 BC, a 40-year reign.")
    ruleset["calendar"]["startYearBc"] = 500
    ruleset["calendar"]["_provenance"]["startYearBc"] = (
        "designed (owner decision D3, 2026-10-21): 500 BC, the Etruscans at their greatest extent and the Roman Republic newly founded.")
    ruleset["victory"]["hardEndYearBc"] = 460
    ruleset["victory"]["_provenance"]["hardEndYearBc"] = (
        "designed (owner decision D3, 2026-10-21): a 40-year reign, twice the classical game's 20, at the same two weeks per turn.")
    ruleset.setdefault("_provenance", {})["_group"] = (
        "designed: a copy of data/rulesets/classical-faithful.json (rulesets have no inheritance), made by tools/Italy/build_italy.py; "
        "only id, name, description, calendar.startYearBc and victory.hardEndYearBc differ.")
    return ruleset

def build_scenario():
    seats = []
    for n in NATIONS:
        if n["id"] == "rome":
            seats.append(dict(nation="rome", control="human", personality=None))
        else:
            aggression, drive = PERSONALITY[n["id"]]
            seats.append(dict(nation=n["id"], control="ai",
                              personality=dict(aggression=aggression, expansionDrive=drive, loyaltyToAlliances=0.5)))
    return dict(
        schemaVersion=1, id=WORLD_ID, name="Italy, 500 BC", worldId=WORLD_ID, rulesetId=WORLD_ID, seats=seats,
        victory=dict(type="totalConquest", goal=None), turnLimit=None, blindHotseat=False, randomSeed=500,
        _provenance={
            "seats": "designed: Rome is the default human seat; New Game lets the player take any of the twelve (owner decision D4). The AI personalities are designed, more aggressive for the Samnites and the Gauls.",
            "victory": "designed: total conquest, the classical rules' own condition, which the ruleset keeps.",
            "turnLimit": "designed: none; the reign ends at 460 BC (the ruleset's victory.hardEndYearBc).",
            "randomSeed": "designed: 500, the start year.",
            "listed": "designed (owner decision D2): offered on New Game.",
        },
        listed=True)

def dump(path, document):
    path.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

def main():
    grid = build_terrain()
    write_png(ROOT / "tools/Italy/italy-500bc.png", grid)
    dump(ROOT / f"data/worlds/{WORLD_ID}.json", build_world(grid))
    dump(ROOT / f"data/rulesets/{WORLD_ID}.json", build_ruleset())
    dump(ROOT / f"data/scenarios/{WORLD_ID}.json", build_scenario())
    print(f"Wrote tools/Italy/italy-500bc.png ({WIDTH}x{HEIGHT}) and data/{{worlds,rulesets,scenarios}}/{WORLD_ID}.json")

if __name__ == "__main__":
    main()
