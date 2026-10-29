DARK MOMBO MOD - MOMBO FANTASMA / GHOST MOMBO          versão / version [1.0.0]
======================================================================================

Mod de fã para Mombo Combo Legacy (demo): transforma a forma Mombo Dino na forma Mombo Fantasma.
Fan-made mod for Mombo Combo Legacy (demo): turns the Dino form into Ghost Mombo.

Feito por / Made by: FeBraine Momboverse
Testado com / Tested with: Mombo Combo Legacy demo [BUILD ID:22062808] | BepInEx 5.4.23.5 (x64, Mono)

Este é um projeto de fã, sem relação com a equipe do jogo. O jogo e sua arte original pertencem aos seus criadores.
This is a fan project, not affiliated with the game's developers. The game and its original art belong to their creators.


######################################################################################
#                                     PORTUGUÊS                                      #
######################################################################################

O QUE O MOD FAZ
---------------
A forma que era o Mombo Dino vira o Mombo Fantasma, com visual novo e habilidades novas.
A árvore de melhorias da forma continua a mesma (7 slots), mas cada slot ganhou um efeito novo:

  1. Investida Fantasmagórica  Mergulho diagonal que explode ao bater no chão, em uma parede ou em um inimigo.
                               Solte o botão da habilidade para cancelar.
  2. Espíritos Vingativos      Inimigos derrotados pela explosão disparam um fragmento de alma para a frente.
  3. Alma de Aço               O fragmento perfura inimigos, voa mais longe e quebra objetos do cenário.
  4. Faminto!                  O fragmento cresce a cada inimigo derrotado (e fica mais lento);
                               o alcance dele recomeça a cada morte.
  5. Explosão Espectral        A explosão ganha um raio ligeiramente maior.
  6. BOO!-M                    Inimigos derrotados pela explosão deixam uma marca que detona uma segunda explosão,
                               menor, quando um inimigo se aproxima ou após 1 segundo.
  7. Último Suspiro            Ao sofrer dano, vira um fantasma por um instante: flutua e atravessa paredes.

REQUISITOS
----------
- Mombo Combo Legacy (demo) para PC (Windows).
- BepInEx 5 (versão x64, Mono). Baixe na página oficial do BepInEx, no GitHub (repositório BepInEx/BepInEx, seção Releases).

INSTALAÇÃO
----------
1. Instale o BepInEx: extraia o conteúdo do .zip dele DENTRO da pasta do jogo, ao lado do MCL.exe.
   Deve ficar assim:  MCL\MCL.exe  e  MCL\BepInEx\  e  MCL\winhttp.dll
2. Abra o jogo uma vez e feche (isso cria a pasta BepInEx\plugins).
3. Extraia ESTE .zip dentro de BepInEx\plugins.  Deve ficar:
       MCL\BepInEx\plugins\DarkMomboMod\DarkMomboMod.dll
       MCL\BepInEx\plugins\DarkMomboMod\textures\ (vários .png)
4. Abra o jogo. A forma aparece onde ficava o Mombo Dino, com o nome Mombo Fantasma.

DESINSTALAÇÃO
-------------
1. Feche o jogo.
2. Apague a pasta  BepInEx\plugins\DarkMomboMod .
O mod não altera nenhum arquivo do jogo; sem a pasta, o Mombo Dino original volta.
(Para remover o BepInEx também: apague a pasta BepInEx, o winhttp.dll, o doorstop_config.ini e o .doorstop_version.)

AVISOS
------
- As melhorias que você já comprou na árvore do Mombo Dino passam a valer para as habilidades do Fantasma.
Se desinstalar o mod, elas voltam ao efeito original. É recomendado fazer um backup do seu save antes de instalar.
- Os textos do mod existem em português e inglês; outros idiomas mostram o texto em inglês.
- As skins do Mombo Dino continuam com a arte original.
- Uma atualização do jogo pode quebrar o mod. Ele foi testado só na versão indicada acima.
- Só para PC. Ainda não há versão para celular.

PROBLEMAS?
----------
- Nada mudou no jogo: confira se existe BepInEx\LogOutput.log depois de abrir o jogo. Se não existir, o BepInEx
  não foi instalado no lugar certo (ao lado do MCL.exe).
- Abra o LogOutput.log e procure a linha que começa com [Ghost] build. Se ela não aparecer, a pasta do mod está no
  lugar errado (precisa estar dentro de BepInEx\plugins).
- Se continuar com problema, avise em [LOCAL DE CONTATO / PAGINA DO MOD] e anexe o LogOutput.log.

CRÉDITOS
--------
Conceituação e execução do Mod: Febraine Momboverse. Programação: Anthropic Claude. Artes do Mombo Fantasma, ícones e efeitos: Febraine Momboverse e Yn3ti.
Obrigado a quem testou: ShadowUltLifeForm.


######################################################################################
#                                      ENGLISH                                       #
######################################################################################

WHAT IT DOES
------------
The form that used to be Dino Mombo becomes Ghost Mombo, with new visuals and new abilities.
The form's upgrade tree is still the same (7 slots), but each slot now has a new effect:

  1. Ghostly Onslaught   Diagonal dive that explodes when it hits the ground, a wall or an enemy.
                         Release the skill button to cancel.
  2. Vengeful Spirits    Enemies defeated by the explosion fire a soul fragment forward.
  3. Soul of Steel       The fragment pierces enemies, flies farther and breaks objects in its path.
  4. King Boo            The fragment grows with every enemy it defeats (and slows down);
                         its range restarts on each kill.
  5. Spectral Blast      The explosion gains a slightly larger radius.
  6. BOO!-M              Enemies defeated by the explosion leave a mark that detonates a smaller second explosion
                         when an enemy gets close, or after 1 second.
  7. Last Breath         After taking damage, you turn into a ghost for a moment: float and pass through walls.

REQUIREMENTS
------------
- Mombo Combo Legacy (demo) for PC (Windows).
- BepInEx 5 (x64 version, Mono). Download it from BepInEx's official GitHub page (repository BepInEx/BepInEx, Releases).

INSTALLATION
------------
1. Install BepInEx: extract the contents of its .zip INTO the game folder, next to MCL.exe.
   You should end up with:  [game folder]\MCL.exe  and  [game folder]\BepInEx\  and  [game folder]\winhttp.dll
2. Start the game once and close it (this creates the BepInEx\plugins folder).
3. Extract THIS .zip into BepInEx\plugins.  You should have:
       MCL\BepInEx\plugins\DarkMomboMod\DarkMomboMod.dll
       MCL\BepInEx\plugins\DarkMomboMod\textures\ (several .png files)
4. Start the game. The form appears where Dino Mombo used to be, named Ghost Mombo.

UNINSTALLATION
--------------
1. Close the game.
2. Delete the folder  BepInEx\plugins\DarkMomboMod .
The mod never changes any game file; without the folder, the original Dino Mombo is back.
(To remove BepInEx too: delete the BepInEx folder, winhttp.dll, doorstop_config.ini and .doorstop_version.)

NOTES
-----
- Upgrades you already bought in Dino Mombo's tree now apply to the Ghost abilities.
  If you uninstall the mod, they go back to their original effect. Back up your save before installing.
- Mod texts exist in Portuguese and English; other languages show the English text.
- Dino Mombo skins keep their original art.
- A game update may break the mod. It was tested only on the version listed above.
- PC only. There is no mobile version.

TROUBLESHOOTING
---------------
- Nothing changed in the game: check that BepInEx\LogOutput.log exists after starting the game. If it does not,
  BepInEx was not installed in the right place (next to MCL.exe).
- Open LogOutput.log and look for the line starting with [Ghost] build. If it is missing, the mod folder is in the
  wrong place (it must be inside BepInEx\plugins).
- If you still have problems, report them at [CONTACT / MOD PAGE] and attach LogOutput.log.

CREDITS
-------
Conceptualization and execution of the Mod: Febraine Momboverse. Coding: Anthropic Claude. Ghost Mombo art, icons and effects: Febraine Momboverse e Yn3ti.
Thanks to the testers: ShadowUltLifeForm.