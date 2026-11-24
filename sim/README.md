# UtopiaSim: núcleo de la simulación en C#

Port del simulador de Processing (en la raíz del repo) a una librería C# **sin dependencias de Godot**.
El mismo código corre en tres lugares:

- **`UtopiaSim.Headless`**: consola sin gráficos, para experimentos largos y por lotes. Guarda CSV.
- **`UtopiaSim.Tests`**: tests automáticos.
- **Godot** ([`../godot`](../godot)): el visor solo lee el estado de `Simulation`.

## Estructura

```
sim/
  configs/               original.json (reproduce el original), etapa.json (gen de etapa preferida),
                         etapa_refugio.json (etapa + refugio parcial de las plantas adultas),
                         dispersion.json (etapa_refugio + fruto, viento y abrojo + dieta de los herbívoros),
                         agua_madrigueras.json (dispersion + agua + 30 madrigueras fijas: base de diploide y cavar),
                         madrigueras_cavadas.json (agua_madrigueras + los herbívoros cavan más: base de arranque),
                         ecosistema.json (lo que mejor funciona; hoy, madrigueras_cavadas)
    experimentos/        comparaciones de mecanismos (--compare)
  UtopiaSim.Core/
    Simulation.cs        el tick: necesidades → buscar objetivo → moverse → comer/beber/aparearse
    Genetics/            formas de los genes: DiskSpace (rueda de color) y PolygonSpace (línea, triángulo, cuadrado...)
    Config/SimConfig.cs  todos los parámetros (los valores por defecto son los del original)
    World/               terreno (toro con ruido periódico) y grilla espacial para búsquedas rápidas
    Creatures/           Creature, SpeciesDef, necesidades
  UtopiaSim.Headless/    programa de consola
  UtopiaSim.Tests/       tests
```

## Cómo correrlo

Requiere el SDK de .NET 8 o superior. Desde la carpeta `sim/`:

```bash
dotnet test                                    # corre los tests

# una corrida de 30 días con la configuración original
dotnet run -c Release --project UtopiaSim.Headless -- -d 30

# 20 corridas (semillas 1..20) de 100 días, guardando también los genes una vez al día
dotnet run -c Release --project UtopiaSim.Headless -- -c configs/etapa.json -d 100 -r 20 --genes

# comparar mecanismos: todas sus combinaciones, mismas semillas (ver "Comparar mecanismos")
dotnet run -c Release --project UtopiaSim.Headless -- --compare configs/experimentos/coexistencia.json

dotnet run -c Release --project UtopiaSim.Headless -- --help
```

Cada corrida es **reproducible**: la misma configuración y la misma semilla dan exactamente el mismo resultado.

## Qué guarda

En `runs/<config>-<fecha>/` (o la carpeta de `-o`):

| Archivo | Contenido |
|---|---|
| `populations.csv` | tick, día, luz y población de cada especie, cada 150 ticks (`--sample-every`) |
| `metrics.csv` | mediciones cada 450 ticks (`--metrics-every`); detalle abajo |
| `deaths.csv` | tabla de supervivencia: muertes por especie, causa, etapa y rango de edad |
| `prey_stage.csv` | tamaño (planta) o peso (animal) de las presas cuando fueron comidas |
| `genes.csv` | con `--genes`: cada ser, cada gen, una vez al día (para dibujar la rueda de color en el tiempo) |
| `summary.csv` | una fila por corrida: población final/mín/máx, día de extinción, nacimientos y muertes por causa |
| `config.json` | la configuración completa usada (con sus `extends` resueltos): basta para repetir la corrida |

### metrics.csv

Medir no cambia la corrida: las métricas no consumen números aleatorios, y hay un test que lo verifica.

| Columnas | Qué miden |
|---|---|
| `{especie}_pop` | población |
| `{especie}_births_per_day`, `{especie}_deaths_{causa}_per_day` | nacimientos y muertes por día en el intervalo. Causas: `starved` hambre, `dehydrated` sed, `eaten` comido, `crowded` semilla que cayó en una casilla llena, `seedling` brote que murió antes de establecerse |
| `{comedor}_food_visible` | presas que ve, en promedio, cada individuo (dentro de su visión; con cobertura, sin contar las escondidas en plantas más allá de `visibleTiles`) |
| `{comedor}_food_right_stage` | de esas, las que tienen el tamaño o peso justo: las podría comer si su color coincidiera |
| `{comedor}_food_edible` | las que de verdad puede comer |
| `{comedor}_food_color_dist` | distancia de color promedio a las presas que ve |
| `{especie}_{gen}_x/_y/_spread/_hue/_pos` | centro del gen en la especie, dispersión (distancia promedio al centro), tono del centro si el gen es la rueda y posición de 0 a 1 si es una línea (para el gen de etapa: 0 temprano, 1 tardío) |
| `{especie}_{gen}_{esquina}` | polígonos de 3 o más lados: peso de cada esquina en el centro de la especie (con `vertexNames`, por ejemplo `planta_dispersion_fruto`) |
| `{planta}_seeds_parent/_wind/_gut/_burr_per_day` | con dispersión: semillas soltadas por día según cómo llegaron (cayeron de la madre, viento, salieron de un estómago, cayeron de un abrojo) |
| `{planta}_seeds_digested`, `_fruits_eaten`, `_fruits_fallen`, `_burrs_attached`, `_burrs_fallen` (`_per_day`) | semillas destruidas en estómagos, frutos comidos o caídos sin comer, abrojos pegados a un animal o caídos |
| `{especie}_in_refuge` | con refugio, madrigueras o cobertura: fracción de la especie que está a salvo (en su zona de refugio o dentro de una madriguera) |
| `{especie}_in_burrow`, `_burrow_use` | con madrigueras: fracción de la especie dentro de una madriguera, y fracción del cupo total reclamada (dentro o en camino) |
| `{especie}_in_cover` | con cobertura: fracción de la especie escondida en una planta grande |
| `{especie}_burrows`, `_burrows_dug_per_day`, `_burrows_collapsed_per_day` | especies que cavan: madrigueras en el mapa, cavadas y derrumbadas por día |
| `{especie}_burrows_dug_fleeing_per_day` | especies que cavan al huir (`fleeDigDays`): de las cavadas, las de emergencia, por día |
| `{especie}_burrows_aged_per_day`, `_ruins` | especies con `collapse`: madrigueras derrumbadas por el tiempo por día, y ruinas que todavía impiden cavar cerca |
| `tilled_tiles`, `tilled_extra_plants` | con tierra revuelta (`collapse.tilledSlots`): casillas con lugar extra para plantas, y cuántas plantas extra caben en total |
| `{especie}_{gen}_hidden_spread`, `_allele_gap` | genética diploide: distancia promedio del alelo oculto al centro de lo que se expresa (compárese con `_spread`) y distancia promedio entre los dos alelos de cada individuo |
| `{planta}_selfed_per_day`, `_unpollinated_per_day` | plantas diploides: semillas autopolinizadas por falta de vecina, y semillas que no se formaron (con `selfing: false`) |
| `{especie}_hunts_failed_per_day` | con territorio: cazas fallidas por rivales cerca |

Para separar las causas del hambre: si `right_stage` es bajo, falla el tamaño; si `right_stage` es alto pero `edible` es bajo, falla el color.

En `deaths.csv`, la etapa `juvenile` significa que nunca se reprodujo (una planta que nunca soltó semilla) y `adult` que sí.

Con varias corridas, cada semilla queda en `seed-N/` y la consola muestra en cuántas sobrevivieron todas las especies.

## Configuraciones que heredan (`extends`)

Una configuración puede partir de otra y cambiar solo lo necesario:

```jsonc
{
  "extends": "original.json",   // ruta relativa a este archivo
  "world": { "maxPlantsPerTile": 4 },
  "species": [ { "name": "carnivoro", "digestDays": 0.5 } ]
}
```

- Los objetos se mezclan campo por campo.
- Las listas de especies y genes se mezclan **por nombre**: aquí solo cambia `digestDays` del carnívoro, y el resto queda como en `original.json`. Un nombre nuevo agrega una especie o un gen.
- Lo demás (números, textos, listas de números) se reemplaza. Un campo en `null` se borra.

## Mecanismos de coexistencia

Todos vienen apagados: sin ellos, la simulación es exactamente el original.

| Mecanismo | Configuración | Qué hace |
|---|---|---|
| **Brotes frágiles** | `plant.seedling` | Desde que aterriza hasta `untilSize`, el brote puede morir en cada tick (causa `seedling`). Riesgo por día = `riskPerDay` + `shadeRiskPerDay` × otras plantas en su casilla + `terrainRiskPerDay` × (1 − qué tan bien crece a esa altura). Una planta establecida ya no muere así: solo muere comida. Conviene subir `world.maxPlantsPerTile`, porque la sombra pasa a hacer de límite suave. |
| **Saciedad** | `digestDays` (especie animal) | Después de comer, no busca comida durante `digestDays` × calorías de esa comida. El hambre sigue corriendo. |
| **Pareja a distancia** | `mateRangeTiles` (especie animal) | Hasta qué distancia encuentra pareja. Por defecto es la visión (`movement.visionTiles`). |
| **Refugio** | `refuge` (especie presa) | Mientras la presa está donde el suelo tiene una altura entre `minHeight` y `maxHeight` (0 lo más bajo del mapa, 1 lo más alto), nadie se la puede comer. El agua llega a ≈ 0.3 con la configuración original: `{ "maxHeight": 0.3 }` es el agua y `{ "minHeight": 0.7 }` los cerros. Con `fleeToRefuge` (por defecto sí), al huir corre al punto de refugio más cercano. |
| **Madrigueras** | `burrows` (especie presa) | `count` sitios fijos en tierra seca, repartidos al azar con la semilla y separados al menos `minSpacingTiles`, donde caben `capacity` individuos cada uno. Al sentirse amenazado corre a la madriguera con cupo más cercana a menos de `searchTiles` y la reclama (el cupo cuenta a los que van en camino); si no hay ninguna, huye como siempre. Dentro (a menos de `enterTiles` del centro) nadie se lo puede comer. Sale cuando otra necesidad pesa más que el miedo. A diferencia del refugio, protege a un número fijo: con muchos herbívoros casi todos quedan afuera; con pocos caben todos. `minHeight`/`maxHeight` limitan dónde se cavan. Se reparten con su propio generador: activarlas no cambia el mapa ni la población inicial. Con `dig`, la especie cava las suyas: un animal que duerme en tierra seca, sin otra madriguera a menos de `minSpacingTiles`, hace una donde está tras `digDays` de sueño (si se mueve, empieza de nuevo), hasta `maxCount` en el mapa; las que nadie reclama en `abandonDays` se derrumban (también las iniciales). Con `dig`, `count` puede ser 0. Con `dig.fleeDigDays`, también cava al huir: si no consiguió madriguera con cupo y el depredador está a más de `fleeSafeTiles` (2), se detiene a cavar una de emergencia donde está y al terminarla se mete; si el depredador se acerca, deja de cavar y arranca. Con `maxColorDistance`, la madriguera es de un solo color: el primero que la reclama le fija su color (gen `colorGene`, por defecto `color`) y solo entran los que estén a esa distancia o menos; vacía, pierde el color. Con `plantSlots`, en su casilla caben esas plantas menos (las que ya están no mueren; solo baja el cupo para las semillas nuevas). Con `collapse`: `halfLifeDays` = vida media (cada madriguera, también las iniciales, dura un tiempo al azar; la mitad se cae antes de ese plazo) y al caerse salen los que estaban dentro; cualquier derrumbe (por tiempo o por abandono) deja una ruina que impide cavar a menos de `minSpacingTiles` durante `ruinDays`, y tierra revuelta: en su casilla caben `tilledSlots` plantas más, acumulable hasta `maxTilledSlots` (para siempre). Cada parte está apagada por defecto. |
| **Cobertura** | `cover` (especie presa) | Las plantas de tamaño ≥ `minPlantSize` esconden hasta `capacity` animales cada una. Al sentirse amenazado, si no consiguió madriguera y el depredador todavía está a más de `visibleTiles`, corre a la planta grande con cupo más cercana a menos de `searchTiles`; si el depredador ya está a `visibleTiles` o menos, esconderse no sirve y sale arrancando. Escondido (a menos de `enterTiles` de la planta) se queda quieto y un cazador solo lo ve a `visibleTiles` o menos, en vez de a toda su visión. No es invulnerable: si el cazador se acerca, lo caza igual. Si se comen la planta o se achica, queda al descubierto. Orden al huir: madriguera, planta, zona de refugio; si no hay ninguna, se aleja del depredador. En `experimentos/cobertura.json` no ayudó en ninguna combinación: reemplaza al agua, que protege más, y al arranque deja a los carnívoros sin presas a la vista. |
| **Llegada tardía** | `arrivalDay` (especie) | Su población inicial (`initialCount`, repartida al azar) aparece ese día en vez de al comienzo. Antes de llegar no cuenta como extinta (ni en `--compare` ni en el visor). Si es la última especie de la lista (como los carnívoros), las demás parten exactamente igual. |
| **Territorio** | `territory` (especie animal) | Rivales = otros de su especie a menos de `radiusTiles`. Con `maxRivalsToBreed`, si tiene más rivales que eso cerca no cría (se revisa cada 20 ticks). Con `interference` > 0, al alcanzar a una presa la caza resulta con probabilidad 1 / (1 + interference × (rivales − tolerance)); si falla, la presa escapa y espera `failCooldownDays`. Ojo: en las pruebas, la interferencia en la caza mató de hambre a los carnívoros, porque se juntan donde están las presas. |

## Dispersión de semillas (gen triangular)

Con `plant.dispersal`, un gen triangular reparte la reproducción de la planta entre tres modos, uno por esquina:

| Esquina | Modo | Qué pasa |
|---|---|---|
| 0 | **fruto** (endozoocoria) | La adulta cuelga un fruto con varias semillas. Un animal con gen de dieta se lo come **sin matar a la planta**. Cada semilla sobrevive al estómago con probabilidad igual a la dieta del animal, y las que sobreviven salen juntas `transitDays` después, donde esté el animal. |
| 1 | **viento** (anemocoria) | La semilla sale disparada hasta `maxRangeFactor` veces más lejos. |
| 2 | **abrojo** (epizoocoria) | La adulta tiene un abrojo con varias semillas que se pega al primer animal que la toca, de cualquier especie. El animal suelta una semilla cada `dropEveryDays`. |

- **Cómo se elige el modo:** cada vez que la planta se reproduce, sortea el modo usando los pesos de su gen como probabilidades.
- **La intensidad crece con el peso:** alcance del viento y semillas por fruto o abrojo = mínimo + (máximo − mínimo) × peso^`exponent`. Con `exponent` > 1 las mezclas rinden poco y conviene especializarse en una esquina; con borde `clamp`, salir de una esquina es difícil.
- **Fruto o abrojo sin dueño:** si la planta ya tiene el máximo (`maxOnPlant`), el más viejo cae bajo la madre como semilla normal.
- **Modo sin bloque:** un modo cuyo bloque (`wind`, `fruit`, `burr`) falta produce una semilla normal. Así se puede probar cada modo por separado.

```jsonc
"genes": [ { "name": "dispersion", "shape": "polygon", "sides": 3, "boundary": "clamp", "vertexNames": ["fruto", "viento", "abrojo"] } ],
...
"plant": { "dispersal": { "gene": "dispersion", "exponent": 2,
  "wind":  { "maxRangeFactor": 4 },
  "fruit": { "maxSeeds": 6, "calories": 0.5, "maxColorDistance": 0.6, "transitDays": 0.1, "maxOnPlant": 3 },
  "burr":  { "maxSeeds": 4, "dropEveryDays": 0.05, "maxOnPlant": 2 } } }
```

### Gen de dieta (fructosa contra celulosa)

Con `"dietGene": "dieta"` (un gen línea), un animal puede comer frutos:

| Dieta | Planta entera | Fruto | Semillas del fruto |
|---|---|---|---|
| 0 = folívoro (celulosa) | todas las calorías | no lo come | — |
| 1 = frugívoro (fructosa) | ninguna | todas las calorías | pasan intactas |
| entre medio | × (1 − dieta)^`dietExponent` | × dieta^`dietExponent` | sobrevive cada una con probabilidad = dieta |

- **Qué come:** al llegar a una planta, el animal come lo que le dé más calorías.
- **Color:** el fruto solo se come si el color de la planta está a lo más a `maxColorDistance` del color del animal. A la planta le conviene parecerse a sus frugívoros y alejarse de los folívoros.
- **`dietExponent` > 1:** el generalista saca menos de ambas cosas, y conviene especializarse.

## Comparar mecanismos

`--compare <experimento.json>` corre **todas las combinaciones** de los mecanismos de un experimento (con 4 mecanismos son 16 variantes) con las mismas semillas, y cuenta en cuántas corridas coexisten todas las especies hasta el final. Cada corrida se detiene en su primera extinción, porque para esta pregunta no hace falta seguir.

```jsonc
// configs/experimentos/coexistencia.json (resumido)
{
  "base": "../original.json",
  "days": 100, "runs": 16, "firstSeed": 1,
  "mechanisms": {
    "saciedad": { "species": [ { "name": "carnivoro", "digestDays": 0.5 } ] },
    "pareja":   { "species": [ { "name": "carnivoro", "mateRangeTiles": 18 } ] }
  }
}
```

Experimentos incluidos:

- `coexistencia.json`: los tres mecanismos más "inicio" (1.000 plantas iniciales). Ninguna combinación coexiste 100 días.
- `refugio_adultas.json`: los mismos mecanismos más "adultas", donde la planta baja a 0.75 al soltar semilla en vez de 0.5 y queda fuera de la ventana comestible. Con el refugio, la saciedad y la pareja a distancia sí ayudan.
- `dispersion.json`: sobre `etapa_refugio.json`, cada modo de dispersión (viento, fruto, abrojo) por separado y combinados.
- `depredadores.json`: sobre `dispersion.json`, refugio de los herbívoros en el agua y territorio de cría de los carnívoros.
- `madrigueras.json`: sobre `dispersion.json`, refugio en el agua contra madrigueras con cupo (30 de 2 lugares).
- `cobertura.json`: sobre `dispersion.json`, todas las combinaciones de agua, madrigueras y cobertura en plantas grandes.
- `diploide.json`: sobre `agua_madrigueras.json`, genética diploide en las plantas, en los herbívoros y en ambos.
- `cavar.json`: sobre `agua_madrigueras.json`, madrigueras cavadas por los herbívoros (con tope de 30 o de 300) contra las 30 fijas.
- `cavar_huyendo.json`: sobre `madrigueras_cavadas.json`, parten 6 madrigueras de 4 lugares y los herbívoros también cavan al huir (`huida`), madrigueras de un solo color (`color`) y una planta menos en su casilla (`casilla`).
- `arranque.json`: sobre `madrigueras_cavadas.json`, cómo evitar que los carnívoros mueran de hambre los primeros días: más carnívoros, más herbívoros o carnívoros que llegan el día 3 (`arrivalDay`). Ninguna ayuda: con más carnívoros coexisten 6/16 (se reparten las presas) y si llegan tarde la caída se atrasa a los días 12-15 (se disparan y se comen casi todo).
- `refugio_etapa.json`: el refugio de las adultas, pero con el gen de etapa (`etapa.json`): un herbívoro que evolucione hacia "tardío" sí puede comérselas. Incluye "lenta", que hace mutar más lento el gen de etapa de los herbívoros.

Cada mecanismo es un parche, con las mismas reglas que `extends`.

En `--compare`, una especie animal con menos de 2 individuos cuenta como extinta: ya no puede reproducirse, y como los animales no mueren de viejos, uno solo podría durar indefinidamente y parecer "vivo". Los resultados de `coexistencia`, `refugio_adultas`, `refugio_etapa` y `dispersion` anotados en sus archivos se midieron antes de este cambio (extinción = 0 individuos); con la regla nueva, los días hasta la primera extinción pueden ser algo menores. `-d`, `-r`, `-s`, `-p` y `-o` reemplazan lo que diga el archivo (por ejemplo, `-r 4 -d 20` para una prueba rápida).

La consola muestra, por variante:

- en cuántas corridas coexistieron todas las especies;
- la mediana de días hasta la primera extinción;
- cuál especie se extinguió primero;
- la población máxima de cada especie (mediana).

Al final muestra el efecto promedio de cada mecanismo: el % de coexistencia de las variantes que lo tienen, contra las que no lo tienen.

En la carpeta de salida queda:

- `comparison.csv`: la misma tabla;
- una carpeta por variante, con su `config.json` (se puede abrir en el visor con `--config`), su `summary.csv` y una carpeta por semilla con todos los CSV de siempre.

## Genes y formas

Cada gen es un punto dentro de una forma. La forma decide cómo se mueven las mutaciones:

```jsonc
"genes": [
  { "name": "color", "shape": "disk", "boundary": "reject" },               // rueda de color
  { "name": "etapa", "shape": "polygon", "sides": 2, "boundary": "clamp" }, // línea
  { "name": "estrategia", "shape": "polygon", "sides": 3, "boundary": "clamp" } // triángulo
]
```

Reglas de borde (qué pasa si una mutación cae fuera):

- **`reject`**: se sortea otra. Los bordes empujan hacia adentro. Así funciona el original.
- **`reflect`**: rebota. Neutral.
- **`clamp`**: queda en el borde. Las esquinas se vuelven pegajosas: desde una esquina de un polígono de N lados, 1/N de las mutaciones vuelve exactamente a ella (línea 50%, triángulo 33%, cuadrado 25%).

`PolygonSpace.VertexWeights(p)` dice cuánto "pertenece" un punto a cada esquina (pesos que suman 1), para leer un gen como mezcla de especialistas.

Cada especie define con qué valor parte cada gen (`initialGenes`) y cuánto muta (`mutation`). Un gen sin `mutation` no muta en esa especie.

### Gen de etapa preferida

Si una especie tiene `"preferredStageGene": "etapa"`, el centro de su ventana de comida deja de ser 0.5 fijo y pasa a ser su gen: 0 = prefiere plantas recién nacidas o presas flacas, 1 = plantas grandes o presas gordas. Con las reglas actuales "tardío" da más calorías y no tiene costo, así que hace falta agregar una contrapartida para que la elección sea interesante.

### Genética diploide (alelos recesivos)

Con `"diploid": {}` en una especie, cada gen tiene **dos alelos**, uno de la madre y otro del padre, y cada alelo tiene una **dominancia** de 0 a 1. Se expresa el alelo más dominante; si dominan igual, el punto medio. El otro queda **oculto**: la selección no lo ve (los herbívoros eligen por el color que se expresa), así que puede derivar lejos y reaparecer cuando se cruzan dos portadores.

- Al nacer, cada padre pasa uno de sus dos alelos al azar. El valor muta como siempre (`mutation`) y la dominancia cambia hasta ± `dominanceStep` (0.05).
- En la población inicial, el primer alelo es el sorteo de siempre, el segundo otro sorteo alrededor de `initialGenes`, y las dominancias son al azar.
- **Animales**: ya se aparean de a dos; `diploid` reemplaza a `inheritance`.
- **Plantas**: se vuelven sexuales. Al formarse cada semilla, el padre es la planta de su especie más cercana a la madre, a menos de `pollenTiles` (3) y de tamaño ≥ `minDonorSize` (0.5). Sin vecina se autopoliniza (`selfing`, por defecto sí); con `"selfing": false` la semilla no se forma (efecto Allee).
- Usa su propio generador al azar: activarlo no cambia el mapa ni las posiciones iniciales.

En el visor, los alelos ocultos aparecen como puntos tenues en el panel de genes, y la ficha muestra el alelo oculto de cada gen.

## Diferencias con el original de Processing

1. **Azar con semilla.** Cada corrida es reproducible. No se reproduce la secuencia aleatoria exacta de Processing.
2. **Terreno.** Ruido Perlin periódico propio. El mundo sigue siendo un toro, ahora sin escalones en el borde.
3. **Un ser que muere en un tick ya no actúa el resto de ese tick.** En el original, un ser comido todavía podía, por ejemplo, aparearse en ese mismo tick.
4. **Comer y aparearse usan la distancia sobre el toro.** En el original fallaban cerca del borde del mapa.
5. **Generaciones de plantas.** Se cuentan bien; en el original siempre valían 1.
6. **Lo puramente visual no está en el núcleo:** sonidos, tripas, cámara y jugador. El visor los hará a partir de `Simulation.EventRaised`.
7. **Búsquedas con grilla espacial.** Encuentran exactamente al mismo "más cercano" que revisar a todos, pero mucho más rápido.

Se conserva a propósito un comportamiento que parece un bug del original: al acercarse a un objetivo, el desplazamiento lateral (strafe) empuja hacia el lado contrario. Está marcado en `Simulation.Pathfind`.

## Usarlo desde Godot (4.6 .NET)

El visor ya existe en [`../godot`](../godot). Así se conecta, por si quieres hacer otro proyecto. En el `.csproj` del proyecto de Godot:

```xml
<ItemGroup>
  <ProjectReference Include="..\sim\UtopiaSim.Core\UtopiaSim.Core.csproj" />
</ItemGroup>
```

Y en un nodo (esquema):

```csharp
Simulation sim;
int ticksPerFrame = 1; // velocidad: 1x, 10x, 100x...

public override void _Ready()
{
    // SimConfig.Load usa rutas de disco: hay que convertir las rutas res:// de Godot.
    string path = ProjectSettings.GlobalizePath("res://../sim/configs/original.json");
    sim = new Simulation(SimConfig.Load(path));
}

public override void _PhysicsProcess(double delta)
{
    for (int i = 0; i < ticksPerFrame; i++) sim.Step();
    // dibujar sim.Creatures (X, Y, Z, Angle, Genes, PlantSize...) y sim.Map
}
```
