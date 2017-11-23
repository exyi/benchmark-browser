## Prohlížedlo performance testů

Program by měl spravovat performance testy - zajistit
nějakou (prioritní) frontu věcí k otestování, zpracovat a uložit
výsledky a hlavně je nějak hezky a užitečně umět zobrazit (s porovnáním
s předchozími běhy, porovnání jednotlivých testů a tak). Primárně mi
jde o [riganti/dotvvm-bechmarks](https://github.com/riganti/dotvvm-benchmarks/) - benchmarky
webového frameworku, které běží několik hodin, je jich asi dvě stě a na
výstupu je docela hodně parametů, [tady je výsledek nějakého běhu](https://ipfs.io/ipfs/QmScnYdY8xoPeHPN85edPdLPbi3GvHrUGicvHAuyMdrAQE/reports/BenchmarkRun-001-2017-05-31-10-34-59/report.html).
Ano, je to html, toto měl být výstup pro člověka, a cílem je to trochu zlepšit.

### UI

Chtělo by to jednak nějaký dashboard projektu, kde by byl vidět historický
vývoj v grafu, případně jak to vypadá pro vybrané aktuální branche
a historické verze (tagy). Pak by bylo dobré mít "detailní porovnání",
které by našlo kde byly největší rozdíly mezi danými verzemi, a ukázalo by,
v jaké části systému asi došlo k problémům, případně kde se povedlo výkon
zlepšit, abych věděl čím se chlubit.

![Dashboard](dashboard-sketch-crop.pdf)

![Porovnání](detail-sketch-crop.pdf)

Další věc je, že ty testy si samy na sebe umí spustit profiler a
naměřit si, kolik procent času se strávilo v určitých funkcích - data
odkázaná výše jsou pořízená Windowsovým PerfView, ale umím to Linuxovým
`perf`em, tak uvidím na čem to poběží. Dlouhodobějším cílem je nicméně tuto
funkcionalitu nějak sjednotit a idálně dostat do benchmarkovací knihovny
BenchmarkDotNet, to už nehodlám dělat v rámci této práce.

Je nicméně skoro škoda z toho zjistit jenom těchto pár čísel, proto
bych chtěl všechny nasbíraná data uložit a pak je umět využít dál.
Technicky to je v podstatě seznam stack-traců, které byly zrovna aktivní,
když se samplovací profiler aktivoval - a počet výskytů konkrétní metody
statisticky vypovídá o tom, kolik času se v ní spálilo. A dají se z toho
generovat FlameGrafy, což by bylo hezké umět v UI tohoto nástroje -
vygenerovat FlameGraf pro konkrétní benchmark, nebo pro všechny položky v Gridu (který bude umět filtrovat)
(FlameGrafy nebudu programovat, je na to Perlové udělátko -
https://github.com/BrendanGregg/FlameGraph). Plus to umí i diferenciální grafy, což by také bylo hezké.
Problém je trochu s velikostí těch performance traců,
ale podle mě to půjde zkomprimovat na jednotky GB na jeden běh testů, nebo i míň.


### Agregace performance traců

Pak by se mi líbilo mít možnost agregovat všechny performance tracy do jednoho
nějakou transformací a moct tak zjistit "kolik se strávilo ve funkcích
classy Repeater?" nebo "Kolik exkluzivního času se strávilo ve funkcích
knihovny Linq?", "Sehnat FlameGraph všech volání funkce GetValue" nebo
"rozdílový FlameGraph všech volání funkce X mezi verzemi 1 a 2".
Nejsem si ale jistý jak tuto feature pojmout, ale pokusím se něco vytvořit,
protože se mi to líbí.

Mohl bych zobrazit grid všech volaných funkcí s možnostmi zanoření, nějakého filtrování (podle knihoven, tříd) a vygenerování grafu, trochu podobně jako to zobrazuje třeba PerfView. Ale uvidím, jak to bude vypadat. Možná bude nejjednodušší jenom vygenerovat soubor zpracovatelný nějakým běžným nástrojem.

A to navíc počítám jenom s tím, že tam budou jenom data z sample profilingu
procesorového času, ale `perf` toho umí sbírat podstatně víc.

### Databáze
Je potřeba ukládat jednak naměřená data a plánovaná spuštění testů. Jedná se o poměrně oddělené věci, ale databázi dělit nehodlám, může být praktické mít relaci například mezi výsledkem a definicí testu. Použiji pravděpodobně PostgreSQL a jeho podporu pro JSONové dokumenty, případně něco koncepčně podobného (ArangoDB je další na řadě).

Potřebuji kolekce projektů v systému, asi uživatele (aby si nemohli jen tak všichni přidávat úlohy do fronty), frontu, log hotových úloh a jejich definice. Definice bude vypadat nějak takto:

* Jméno úlohy
* Pod jaký spadá projekt
* Jak se má spustit (build script + repozitář + parametry)

A hlavně potřebuji kolekci výsledků, kde bude:

* Pro jakou verzi byl test spuštěn (commit hash)
* Pod jakou spadá úlohu, v jaké verzi a v jaké verzi bylo repo s build scriptem
* Environment - OS, Hardware, verze .NETu
* Výsledky - obecně cokoliv, sada čísel (čas, deviace, alokovaná pamět, odkazy na nasbírané stacky)

Protože pracuji s verzemi v gitu, potřebuju mít nějakou možnost porovnávat verze, zobrazit je a spouštět pro ně verze, tak si jednoduše pořídím naklonovaný repozitář. Nechce se mi přeimplementovávat git v databázi ani být závislý na GitHub API, tak mi připadá spustit `git` jako nejpříčetnější možnost.

Uložení nasbíraných stacků nechci řešit standardní databází, protože je poměrně hodně dat a jsem si jistý, že to půjde dobře zkomprimovat specializovaným algoritmem. Ukládaná data jsou multiset nasbíaných stacků (pořadí zahazuji, stejně se ten test spouští pořád dokola). S tím, že části těch stackú budou velmi duplicitní mezi různými běhy benchmarků, takže to půjde všechno mergnout a pak napsat odděleně jenom jejich počty. Nebo něco na ten způsob, toto bude potřeba trochu prozkoumat.

### Průběh testování

Spuštění testů a získání výsledků by neměl být velký technologický ani návrhový problém, vzhledem k tomu, že už to mám v podstatě rozchozené. BenchmarkDotNet umí exportovat podrobná data do různých formátů, takže jenom potřebuji rozparsovat performance tracy. Tento systém by měl:

* Naklonovat/pullnout repozitář s testy
* Checkoutnout zadanou verzi testovaného subjektu
* Spustit testy
* Sesbírat výsledky
* Připadně průběžně odesílat stav (do BenchmarkDotNetu na to dám plugin, nechci to brát ze STDout)

Navíc je docela důležité, aby testy běžely na jiném počítači (fyzicky), takže tu appku budu muset rozdělit na dvě a posílat si data přes síť.

### Technologie

Co se týče technologií, tak ty benchmarky jsou v .NETu a zbytek hodlám
také napsat v .NETu, jako serverovou aplikaci s webovým frontendem. A
demonstraci bych nejraději provedl s běžící appkou s reálnými daty ;) A
byl bych určitě rád, kdyby tím šly pouštět i jiné benchmarky, minimálně
 cokoliv postavené na knihovně BenchmarkDotNet, idálně i jiné úplně
jiné věci, třeba Rustový `cargo bench` nebo aby to umělo tahat data z
jiných zdrojů (třeba browserové metriky z UI testů).

Konkrétně hodlám napsat webserver v F# + Asp.Net Core, s tím, že tam bude asi jenom API
a zobrazování bude řešené kompletně v browseru. Chci zkusit použít Fable Elmish -
kompilátor z F# do Javascriptu s nástavbou na React. Vypadá to jako docela cool
cesta jak psát UI funkcionálně, ale nejsem si jistý, že to půjde dobře, přeci jenom
je to dost hipsteřina, tak to možná vzdám a napíšu jinak.
