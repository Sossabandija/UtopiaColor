# Visor 2D en Godot

Visor de la simulación de [`../sim`](../sim). El visor no tiene lógica propia: dibuja lo que calcula
`UtopiaSim.Core`, así que con la misma configuración y semilla ves exactamente la misma corrida que
produce la consola (`UtopiaSim.Headless`).

## Abrirlo

La forma más simple es con doble clic:

- **`abrir_visor.bat`**: compila y abre el visor directamente.
- **`abrir_editor.bat`**: abre el editor de Godot en este proyecto. En el editor no se ve nada porque la pantalla se arma por código; presiona **F5** (o ▶) para ejecutarlo.

Ambos buscan Godot en `Documentos\Godot_v4.6.3-stable_mono_win64\...`. Si lo mueves, define la variable de entorno `GODOT` con la ruta al `.exe`.

Desde el administrador de proyectos de Godot: *Importar* → `godot/project.godot` → *Editar* → **F5**.

### Depurar desde VS Code (F5)

Abre la carpeta del repo en VS Code y presiona **F5**: compila el visor y abre Godot con el depurador de C# conectado. Los breakpoints funcionan en `godot/src` y en la simulación (`sim/UtopiaSim.Core`). Requiere la extensión **C#** (`ms-dotnettools.csharp`); VS Code la sugiere al abrir el repo.

En la vista *Ejecutar y depurar* (Ctrl+Shift+D) hay tres opciones:

| Opción | Qué hace |
|---|---|
| **Visor Godot (debug)** | Abre el visor como siempre. |
| **Visor Godot: elegir configuración y semilla (debug)** | Pregunta la configuración (por defecto `ecosistema.json`) y la semilla antes de abrir. |
| **Consola: simulación (debug)** | Corre la simulación sin gráficos, 5 días de `ecosistema.json`, y guarda los resultados en `sim/runs/debug`. |

Las tres configuraciones están en `.vscode/launch.json`. Si Godot está en otra carpeta, cambia `"program"` en las dos opciones del visor. Ctrl+Shift+B solo compila.

Las configuraciones se leen de `sim/configs/*.json`. Si editas un JSON, basta con apretar **Reiniciar**: no hay que recompilar.

### Si Godot se ve todo gris

En algunos PCs las ventanas que usan OpenGL se ven grises: Godot dibuja, pero la imagen no llega a la pantalla. Por eso el proyecto usa **Forward+ con Vulkan**, no "Compatibility".

El administrador de proyectos de Godot 4.6 usa OpenGL, así que puede verse gris. En ese caso ábrelo con Vulkan:

```
Godot_v4.6.3-stable_mono_win64.exe --rendering-method forward_plus --rendering-driver vulkan
```

O usa los `.bat` de arriba, que no pasan por el administrador.

## Pantalla

- **Mundo** (izquierda): terreno visto desde arriba. El agua es azul y el relieve está sombreado, con colores apagados a propósito para que resalten los seres.
  - Plantas: círculos. El tamaño indica cuánto han crecido.
  - Animales: flechas que apuntan hacia donde miran. El relleno es el color de su gen y el borde el de su especie. Más gordo = más lleno.
  - El mundo es un toro: alrededor del mapa se dibujan copias atenuadas de lo que hay al otro lado.
  - Con dispersión de semillas, las plantas muestran sus frutos (punto rojo-anaranjado) y abrojos (punto pajizo), más grandes mientras más tengan.
  - Con madrigueras: círculos con borde de tierra clara. Vacías son negras y se van llenando con el color de su especie según el cupo reclamado (los que están dentro y los que van corriendo hacia ella).
  - Un animal escondido (en una madriguera o en una planta) se dibuja semitransparente.
  - De noche todo se oscurece un poco (tecla N para quitarlo).
- **Panel** (derecha):
  - Configuración, semilla y **Reiniciar**.
  - Velocidad: 1× equivale a 30 ticks/s, como el original. **Máx** simula todo lo que puede sin trabar la ventana.
  - **Gráfico** (se elige arriba, o con la tecla C):
    - *Población*: cada especie desde el inicio, en escala log (para ver 3.000 plantas y 5 carnívoros juntos) o lineal. Marca el día en que se extinguió cada especie.
    - *Comida a la vista*: para cada especie que come, cuántas presas ve cada individuo, cuántas tienen el tamaño justo y cuántas son comestibles. Si "tamaño justo" es alto pero "comestibles" es bajo, el problema es el color.
    - *Nacimientos y muertes*: por día y por causa (hambre, sed, comidos, sin espacio, como brote). Solo aparecen las causas que ocurrieron.
    - *Etapa preferida por especie* (con gen de etapa): 0 = prefiere presas tempranas, 1 = tardías. La línea gris marca desde qué etapa un herbívoro alcanza a las plantas que ya soltaron semilla: si la curva de herbívoros la cruza, se están comiendo a las adultas.
    - *Presas en su refugio (%)* (con refugio, madrigueras o cobertura): qué parte de cada especie está a salvo (en su zona de refugio o en una madriguera). Con madrigueras, además, qué parte está dentro de una y qué parte del cupo está reclamada; con cobertura, qué parte está escondida en una planta (ahí solo se la ve de cerca, pero se la puede cazar).
    - *Dieta por especie* (con gen de dieta): 0 = folívoro, 1 = frugívoro.
    - *Gen dispersion: planta* (y cualquier gen de 3 o más esquinas): el peso promedio de cada esquina en el tiempo.
    - *Semillas por modo: planta* (con dispersión): cuántas semillas por día cayeron de la madre, volaron con el viento, salieron de un estómago o cayeron de un abrojo, y cuántas se destruyeron en estómagos.
    - *Tono de color por especie*: el tono promedio en el tiempo. Ahí se ve la carrera circular: las plantas escapan por la rueda y los herbívoros las persiguen.
    - *Distancia de color a la comida*: qué tan lejos en la rueda está, en promedio, la comida que ve cada especie.
    - *Variación de color: expresada y oculta* (con genética diploide): qué tan lejos del color promedio de la especie están lo que se expresa y los alelos ocultos. Si "oculta" crece más que "expresada", la especie está guardando colores que la selección no ve.
  - **Genes**: la forma de cada gen con un símbolo por ser (X planta, □ herbívoro, ○ carnívoro):
    - Rueda: la rueda de color del original.
    - Polígono: sus esquinas, con nombre si el gen tiene `vertexNames` (por ejemplo fruto, viento, abrojo).
    - Línea: un histograma por especie con su media.
    - Con genética diploide, los alelos ocultos aparecen como puntos tenues (en la línea, como un contorno). Del ser seleccionado, una línea punteada une lo que expresa con su alelo oculto.
- **Ficha** (abajo a la izquierda, al hacer clic en un ser): qué está haciendo, padres, genes y la urgencia de cada necesidad. La necesidad que atiende va enmarcada. En el mundo se dibuja la línea hacia su objetivo y su rango de visión.
  - Con brotes frágiles, un brote muestra su riesgo de morir por día.
  - Con saciedad, un animal que acaba de comer muestra cuántas horas le quedan sin cazar.
  - Con refugio, madrigueras, cobertura y territorio: si está en su refugio, si huye hacia él, hacia su madriguera o hacia una planta, si está escondido, si tiene demasiados rivales cerca para criar o si espera tras una caza fallida.
  - Con genética diploide, cada gen muestra también su alelo oculto (o "codominantes" si los dos dominan igual).
  - Con dispersión, una planta muestra cómo llegó (de su madre, con el viento, en un estómago o en un abrojo) y cuántos frutos y abrojos tiene; un animal, su dieta y cuántas semillas y abrojos lleva.

## Controles

| Tecla / ratón | Acción |
|---|---|
| Arrastrar | Mover la cámara |
| Rueda | Zoom |
| Clic | Ver la ficha de un ser |
| F | Seguir al ser seleccionado |
| Esc | Soltar la selección |
| Espacio | Pausa / continuar |
| Flechas arriba / abajo | Más / menos velocidad |
| R | Encuadrar el mapa completo |
| N | Activar / quitar la noche |
| C | Mostrar el siguiente gráfico |
| G | Mostrar el siguiente gen |

## Línea de comandos

Para abrir directo en un estado o guardar capturas sin tocar nada (las opciones van después de `--`):

```bash
Godot_v4.6.3-stable_mono_win64_console.exe --path godot -- \
  --config etapa.json --seed 9 --advance-days 3 --select herbivoro --zoom 0.5 --gene etapa \
  --screenshot captura.png
```

| Opción | Qué hace |
|---|---|
| `--config <archivo>` | Configuración (nombre en `sim/configs` o ruta; sirve el `config.json` de una variante de `--compare`) |
| `--seed <n>` | Semilla |
| `--advance-days <d>` | Simula d días antes de mostrar |
| `--select <especie>` | Selecciona al primer ser de esa especie |
| `--zoom <z>` | Zoom inicial (y sigue al seleccionado) |
| `--gene <nombre>` | Gen que se muestra en el panel |
| `--speed <0-5>` | Pausa, 1×, 10×, 30×, 100×, Máx |
| `--chart <n>` | Gráfico inicial (0 = población, en el orden del selector) |
| `--screenshot <ruta.png>` | Guarda una captura a los ~1,5 s y cierra |

## Estructura

```
godot/
  project.godot, Main.tscn   escena mínima: un nodo con Main.cs; el resto se arma por código
  src/Main.cs                interfaz, velocidad, configuración, línea de comandos
  src/WorldView.cs           terreno, seres (MultiMesh: miles de seres en pocas llamadas), cámara, clic
  src/SelectionOverlay.cs    anillo, línea al objetivo y rango de visión del seleccionado
  src/TimeSeriesChart.cs     gráfico de curvas en el tiempo (población, comida, muertes, color)
  src/PopulationHistory.cs   poblaciones en el tiempo (cada 30 ticks)
  src/MetricsHistory.cs      mediciones de SimMetrics en el tiempo (cada 450 ticks)
  src/GeneView.cs            forma de un gen con los seres encima
  src/InspectorView.cs       ficha del ser seleccionado
```
