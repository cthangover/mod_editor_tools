# Editor Tools — crear escenas y escenarios dentro del juego en marcha

Editor Tools son las ventanas de autor que se abren sobre el juego en marcha: el editor de escenas, el de escenarios, los editores del mapa del mundo, de objetos interactivos y de luces, un constructor de escenas de Godot y la exportación al catálogo de un mod. Todo lo que guardan son archivos de mod normales, y el juego toma el cambio al instante, así que puedes escribir un lugar, entrar en él y jugarlo sin reiniciar.

El mod también contiene la base de los editores de objetos y de personajes (que son mods aparte: Items Editor y Characters Editor). Un juego obtiene estas herramientas mediante el conjunto Developer Tools o con su propia colección.

## Cómo abrir las herramientas

Dentro de un juego, el botón del martillo de la barra de herramientas abre la caja de herramientas:

![La barra de herramientas de la plantilla de novela con el martillo pulsado y la caja de herramientas abierta: Interactive Editor, Light Editor, Map Editor, Scene editor.](assets/toolbox.jpg)

- **Editor de escenas**, **Editor de mapas**, **Editor de interactivos** y **Editor de luces** están en la caja. Otros mods de herramientas añaden ahí sus propias entradas (los editores de objetos, de personajes y de mapas RPG, el editor de datos).
- El **editor de escenarios** se abre desde el editor de escenas: elige una escena, selecciona uno de sus escenarios y pulsa **Abrir**, o **+ Nuevo escenario**.
- Si las herramientas se cargan en el menú principal (por ejemplo con `--all-mods`), cada una tiene también un botón en el bloque de herramientas del menú, incluidos el constructor de escenas y la exportación al catálogo.

Las ventanas siguen abiertas al cambiar de escena, se pueden mover y redimensionar, y se cierran con la cruz.

## Una edición pequeña completa: un lugar nuevo

La plantilla de novela tiene un pueblo, un bar y una casa. Aquí añadiremos un parque al que se llega desde el pueblo usando solo los editores.

**1. Una escena nueva.** En el editor de escenas pulsa **+ Nueva escena**, escribe el nombre, elige el mod donde guardarla y añade un fondo:

![El editor de escenas: a la izquierda, las escenas my_novel_bar, my_novel_home y my_novel_town de my_novel_game; en el centro, el fondo home/vestibule añadido a la escena; a la derecha, Nombre my_novel_park, Guardar en my_novel_game, Sonido ninguno, Escenario por defecto ninguno.](assets/new-scene.jpg)

Guardar (Ctrl+S) escribe `scenes/my_novel_park.json` en `my_novel_game`; el juego conoce la escena al instante.

**2. Su escenario.** **+ Nuevo escenario** abre el editor de escenarios con un archivo para la escena. El parque ofrece una única salida:

![El editor de escenarios con my_novel_park.scenario: escena my_novel_park, prioridad 10, la narración «Un parque tranquilo detrás del pueblo. Hojas mojadas, un banco vacío.», la elección «¿Qué hago ahora?» con «Volver al pueblo» que lleva a switch_scene my_novel_town.](assets/park-scenario.jpg)

**3. Un camino hasta allí.** En el escenario del pueblo se añaden una respuesta y una etiqueta:

![El editor de escenarios con my_novel_town.scenario: la elección «¿Adónde ir?» tiene ahora la respuesta «Ir al parque» -> :park, y la etiqueta :park hace switch_scene my_novel_park.](assets/town-scenario.jpg)

```scenario
option "Ir al parque" -> :park

:park
switch_scene my_novel_park
end
```

Guardar un escenario descarta las cachés de escenarios del juego, así que la próxima entrada al pueblo ya reproduce el texto nuevo.

**4. Juégalo.** **Ir a la escena** en el editor de escenas, o simplemente seguir jugando, entra en el pueblo, y la nueva respuesta ya está ahí:

![El pueblo en el juego en marcha: «¿Adónde ir?» con Entrar en el bar, Ir al parque, Volver a casa.](assets/town-choice.jpg)

![El parque nuevo tras elegir Ir al parque: «¿Qué hago ahora?» con Volver al pueblo.](assets/park.jpg)

## Los editores

- **Editor de escenas**: los lugares de una novela: nombre, el mod donde guardar, fondos (al entrar se elige uno al azar), sonido ambiente con botón de escucha, el escenario por defecto y los escenarios de la escena. **Ir a la escena** termina el diálogo en curso y entra en la escena.
- **Editor de escenarios**: los archivos de todos los mods, un resumen y un grafo de escenas y transiciones; resaltado de DSL; Ctrl+S guarda; **Jugar** (F5) guarda y reproduce el escenario dentro del juego en marcha, y Detener vuelve a donde estaba el juego; **Abrir en un editor externo** abre el archivo en el editor del sistema.
- **Editor de mapas**: las zonas del mapa del mundo.
- **Editor de interactivos**: los objetos clicables de la escena actual.
- **Editor de luces**: las luces de la escena actual.
- **Constructor de escenas**: escenas `.tscn` de Godot para los mods.
- **Exportación al catálogo**: empaqueta un mod en un zip y escribe su entrada para un catálogo de mods con sumas de comprobación.

Deshacer funciona mientras editas (Ctrl+Z en el texto y en los formularios de los editores de objetos y personajes). Comprobar un mod en busca de referencias rotas es tarea de Validator; los editores no te impiden guardar un escenario con un error.

## De quién son los archivos que se escriben

Las herramientas solo escriben en mods instalados como carpetas. Una escena es la suma de los archivos `scenes/<name>.json` de todos los mods, así que el editor de escenas escribe el archivo del mod elegido en **Guardar en**. Si cambias una escena que pertenece a otro mod o a un mod empaquetado, se crea un archivo en tu mod que la amplía; los fondos que vienen de otros archivos se muestran bloqueados. Los escenarios se guardan en el mod al que pertenecen.

## Si algo sale mal

1. **No aparece el botón del martillo.** El juego no incluye las herramientas; añade el conjunto Developer Tools a su colección o ejecútalo con `--with-mods=editor_tools`.
2. **Un escenario nuevo no se reproduce.** Su cabecera nombra otra escena, o antes se elige otro escenario de la escena (compara sus `priority`).
3. **Guardar en no muestra ningún mod.** Solo se puede escribir en mods instalados como carpetas.
