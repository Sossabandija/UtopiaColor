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
                         ecosistema.json (dispersion + refugio de los herbívoros en el agua: lo que mejor funciona)
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
| `{comedor}_food_visible` | presas que ve, en promedio, cada individuo (dentro de su visión) |
| `{comedor}_food_right_stage` | de esas, las que tienen el tamaño o peso justo: las podría comer si su color coincidiera |
| `{comedor}_food_edible` | las que de verdad puede comer |
| `{comedor}_food_color_dist` | distancia de color promedio a las presas que ve |
| `{especie}_{gen}_x/_y/_spread/_hue/_pos` | centro del gen en la especie, dispersión (distancia promedio al centro), tono del centro si el gen es la rueda y posición de 0 a 1 si es una línea (para el gen de etapa: 0 temprano, 1 tardío) |
| `{especie}_{gen}_{esquina}` | polígonos de 3 o más lados: peso de cada esquina en el centro de la especie (con `vertexNames`, por ejemplo `planta_dispersion_fruto`) |
| `{planta}_seeds_parent/_wind/_gut/_burr_per_day` | con dispersión: semillas soltadas por día según cómo llegaron (cayeron de la madre, viento, salieron de un estómago, cayeron de un abrojo) |
| `{planta}_seeds_digested`, `_fruits_eaten`, `_fruits_fallen`, `_burrs_attached`, `_burrs_fallen` (`_per_day`) | semillas destruidas en estómagos, frutos comidos o caídos sin comer, abrojos pegados a un animal o caídos |
| `{especie}_in_refuge` | con refugio: fracción de la especie que está en su refugio |
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
