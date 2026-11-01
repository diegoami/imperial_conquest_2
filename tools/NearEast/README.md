# Near East campaign authoring

T273 / #1712: historical openings around 1350 BC and 750 BC, using the existing
engine. The complete source/abstraction brief is
[near-east-campaigns.md](../../docs/design/near-east-campaigns.md).

## Rebuild and validate

Normal rebuild is offline and uses the committed small source subsets:

```powershell
python -X utf8 tools/NearEast/build_campaigns.py
dotnet run --project tools/TerrainImport -- import tools/NearEast/near-east.png --world data/worlds/near-east-1350bc.json
dotnet test tests/IC2.Engine.Tests --filter FullyQualifiedName~NearEastCampaignTests --nologo -v q
```

Both worlds share the generated terrain sidecar. The PNG uses the existing
T201 palette and river rules. Existing campaign documents/IDs are untouched.
Campaigns run for twenty years under the existing calendar and total-conquest
condition. Starting forces/economics, source-scale approximations and political
compression are designed, not historical troop counts, censuses or borders.

`cities.csv` is the curated authoring selection, with separate period captions
and owners. `sources/sites.json` carries the location, precision, dated resource
references and copyright/licence attribution. `sources/chronology.json` adds
explicitly labelled contextual ranges when a period overview supplies evidence
that the GIS resource bounds omit. These ranges do not prove exact-year urban
continuity or political control. Missing evidence makes `prepare_sites.py`
refuse to publish the subset. Known later foundations and abandoned phases are
not inserted merely because a coarse database interval spans the opening.

## Sources, attribution and changes

**Pleiades:** copyright the Pleiades contributors; project editors and individual
record credits are linked from the canonical place resources. Adapted under
[Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/).
[Pleiades credits](https://pleiades.stoa.org/credits),
[reuse guidance](https://pleiades.stoa.org/help/help-and-information),
[GIS source](https://atlantides.org/downloads/pleiades/gis/).
The downloaded GIS snapshot has archive entries dated 2026-10-30, retrieved
2026-11-01; its public SHA-256 is retained in `sites.json`. The full archive is
not a build dependency and stays in ignored `rendered/near-east-sources/`.
The subset retains original provenance citations, canonical resource links and
coordinate/date precision. Changes: curated selection, conventional display
names, tile projection, explicit regional grouping and designed balance.
Source contributors do not endorse this adaptation.

To re-extract this source selection, place the referenced archive at
`rendered/near-east-sources/pleiades_gis_data.zip` and run
`python -X utf8 tools/NearEast/prepare_sites.py`. Upstream updates are a source
revision to review, not a silent reproducible rebuild of the captured snapshot.

**Natural Earth:** public-domain v5.1.2 vector data at commit
`f1890d9f152c896d250a77557a5751a93d494776`.
[Terms](https://www.naturalearthdata.com/about/terms-of-use/).
The land, rivers/lake-centrelines and lakes source URLs, Git blob identifiers
and SHA-256 hashes are recorded in `sources/geography.json`. Made with Natural
Earth; attribution to Tom Patterson, Nathaniel Vaughn Kelso and contributors.
`prepare_geography.py` clips and rounds those vectors into the committed subset.
Its downloaded inputs belong in the same ignored source folder.

Changes and limits: modern coastline is a coarse approximation for antiquity;
relief and vegetation are authored abstractions, not a palaeoenvironmental
reconstruction. Only selected natural lakes are restored, excluding modern
reservoirs; modern canals are excluded. Narrow real straits and tiny inhabited
islands need a minimum tile footprint for the existing movement/landing rules.
Inland navigable water uses the existing Italy water/fleet convention. No map
art, original-game data or private owner files are copied.

## Political and administrative abstractions

The owner approved labelled regional centres at dated archaeological sites for
poorly documented tribal regions on 2026-11-01. Their administrative role,
ownership and population are explicit abstractions, not claims of a known
city, king or capital in the exact opening year.

The nation budget is at most fifteen regular seats plus the barbarian role.
Mycenaean/Aegean centres, local Syrian/Canaanite rulers, western Anatolian
polities and Iranian highland communities are therefore grouped. In 750 BC,
Tabal/Cilicia/Melid share the Neo-Hittite seat so Philistia can remain distinct
from Judah; Phrygia/Lydia share another regional seat. Neither is a claimed
unified empire. Egypt's 750 BC seat represents decentralized Egyptian polities.
Median communities are not the later Median Empire, and there is no
Achaemenid Persian state in either opening.

Tell Fekheriye is Mitanni's chosen gameplay administrative centre, without
asserting the debated identification of Washukanni. Enkomi is the Alashiya
seat's chosen centre, not a proved island capital. Marlik, Godin and Qalaichi
regional centres do not establish an urban capital or personal ruler.
Institutional leader titles avoid inventing historical people.

Opening regular-state relations are neutral gameplay defaults, not a precise
reconstruction of treaty/vassal obligations. Egyptian garrison/vassal influence
and Canaanite/Syrian local rulers are compressed into playable owners; ownership
is always tagged as designed. Later turns are not scripted historical outcomes.

Unit labels/art are proxies for the existing five archetypes. The numerical
unit and combat rules are identical to the existing Italy/classical rules;
the calendar is configured for the selected opening. Period presentation does
not add a chariot combat system or stronger Computer general. Barbarian force
composition and arrival tuning reuse the approved Italy provisional setup.
