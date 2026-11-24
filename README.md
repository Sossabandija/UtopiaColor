# UtopiaColor

Simulador de cadena alimenticia (plantas, herbívoros y carnívoros) cuyos genes evolucionan.

| Carpeta | Qué es |
|---|---|
| raíz (`*.pde`) | Simulador original en Processing (3D) |
| [`sim/`](sim) | Núcleo de la simulación en C#, sin gráficos: configuración en JSON, genes con forma, experimentos por lote en consola, tests |
| [`godot/`](godot) | Visor 2D en Godot 4.6 .NET que usa ese núcleo |

## Simulador original (Processing)

Controls for this food chain simulator!

| First Header  | Second Header |
| ------------- | ------------- |
| ENTER | Toggle whether the mouse is locked into the screen (mouse movements change camera direction) or is free again |
| 'WASD' | Move around the gray stick figure viewer guy |
| SPACE | Make the viewer guy jump |
| 'C' | Toggle whether you want the camera to follow a nearby creature (cow or carnivore) or the viewer guy |
| Up and Down | Change the playback speed (1x by default, 10+ speed is good for getting to the next day faster) |
| 'T' | If the camera is currently tracking an animal, pressing 'T' will shift the camera focus to that animal's target (e.g. if a carnivore is chasing a cow) |
| 'U' | Hold down U to rotate the camera at a medium speed for cinematic effect |
| SHIFT | Press SHIFT to toggle whether the camera should rotate at a slow speed for cinematic effect |
| Mouse Wheel | Zoom in and out |
