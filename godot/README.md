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
  - De noche todo se oscurece un poco (tecla N para quitarlo).
- **Panel** (derecha):
  - Configuración, semilla y **Reiniciar**.
  - Velocidad: 1× equivale a 30 ticks/s, como el original. **Máx** simula todo lo que puede sin trabar la ventana.
  - **Población**: curva de cada especie desde el inicio, en escala log (para ver 3.000 plantas y 5 carnívoros juntos) o lineal. Indica en qué día se extinguió cada especie.
  - **Genes**: la forma de cada gen con un símbolo por ser (X planta, □ herbívoro, ○ carnívoro):
    - Rueda: la rueda de color del original.
    - Polígono: sus esquinas numeradas.
    - Línea: un histograma por especie con su media.
- **Ficha** (abajo a la izquierda, al hacer clic en un ser): qué está haciendo, padres, genes y la urgencia de cada necesidad. La necesidad que atiende va enmarcada. En el mundo se dibuja la línea hacia su objetivo y su rango de visión.

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
| `--config <archivo>` | Configuración (nombre en `sim/configs` o ruta) |
| `--seed <n>` | Semilla |
| `--advance-days <d>` | Simula d días antes de mostrar |
| `--select <especie>` | Selecciona al primer ser de esa especie |
| `--zoom <z>` | Zoom inicial (y sigue al seleccionado) |
| `--gene <nombre>` | Gen que se muestra en el panel |
| `--speed <0-5>` | Pausa, 1×, 10×, 30×, 100×, Máx |
| `--screenshot <ruta.png>` | Guarda una captura a los ~1,5 s y cierra |

## Estructura

```
godot/
  project.godot, Main.tscn   escena mínima: un nodo con Main.cs; el resto se arma por código
  src/Main.cs                interfaz, velocidad, configuración, línea de comandos
  src/WorldView.cs           terreno, seres (MultiMesh: miles de seres en pocas llamadas), cámara, clic
  src/SelectionOverlay.cs    anillo, línea al objetivo y rango de visión del seleccionado
  src/PopulationChart.cs     curva de población
  src/GeneView.cs            forma de un gen con los seres encima
  src/InspectorView.cs       ficha del ser seleccionado
```
