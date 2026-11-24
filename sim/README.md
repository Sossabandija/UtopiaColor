# UtopiaSim: núcleo de la simulación en C#

Port del simulador de Processing (en la raíz del repo) a una librería C# **sin dependencias de Godot**.
El mismo código corre en tres lugares:

- **`UtopiaSim.Headless`**: consola sin gráficos, para experimentos largos y por lotes. Guarda CSV.
- **`UtopiaSim.Tests`**: tests automáticos.
- **Godot** (fase siguiente): el visor solo leerá el estado de `Simulation`.

## Estructura

```
sim/
  configs/               original.json (reproduce el original), etapa.json (gen de etapa preferida)
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

dotnet run -c Release --project UtopiaSim.Headless -- --help
```

Cada corrida es **reproducible**: la misma configuración y la misma semilla dan exactamente el mismo resultado.

## Qué guarda

En `runs/<config>-<fecha>/` (o la carpeta de `-o`):

| Archivo | Contenido |
|---|---|
| `populations.csv` | tick, día, luz y población de cada especie, cada 150 ticks (`--sample-every`) |
| `genes.csv` | con `--genes`: cada ser, cada gen, una vez al día (para dibujar la rueda de color en el tiempo) |
| `summary.csv` | una fila por corrida: población final/mín/máx, día de extinción, nacimientos y muertes por causa |
| `config.json` | copia de la configuración usada |

Con varias corridas, cada semilla queda en `seed-N/` y la consola muestra en cuántas sobrevivieron todas las especies.

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
