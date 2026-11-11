# AGENTS.md — Contexto Permanente do Projeto: Aska Multiple Eye (Modo Age of Empires)

## 1. Visão Geral e Objetivo do Mod

O **Aska Multiple Eye** transforma o multiplayer cooperativo do **ASKA** (Unity `6000.3.12f1` IL2CPP + Photon Fusion + BepInEx `6.0.0-be.791`) em uma experiência estilo **Age of Empires**, onde:

- **1 Jogador = 1 Aldeia Independente (`NetworkedCreatorUserId`)** desde o segundo `0` da partida, dividindo o mesmo mapa, mas com **progresso, trabalhadores, HUD e território independentes**.
- **Aliança Cooperativa entre Jogadores**:
  - **Os Jogadores (personagens controlados por humanos)** podem visitar a aldeia aliada, ajudar a entregar materiais, bater o martelo para ajudar a construir e usar fogueiras/bancadas aliadas (vendo na bancada apenas as receitas que o próprio jogador já desbloqueou).
  - **As Construções** pertencem **100% a quem colocou a planta (blueprint) no chão** (`structure.NetworkedCreatorUserId`). Mesmo que um aliado ajude a construir, a estrutura pronta pertence e conta exclusivamente para o dono da planta.
  - **Os Aldeões (NPCs)** pertencem **100% ao dono do Olho de Odin onde foram invocados** e **jamais** trabalham, constroem, dormem, comem ou guardam itens em estruturas criadas por outro jogador.

---

## 2. Regras Críticas de Segurança e Diretrizes (NUNCA VIOLAR)

1. **NUNCA criar `steam_appid.txt`** nem modificar/tocar em arquivos originais do jogo ou da Steam (`Aska.exe`, `GameAssembly.dll`, `UnityPlayer.dll`, `baselib.dll`, `steam_api64.dll`).
2. **NUNCA compilar ou sobrescrever `AskaMultipleEye.dll` enquanto o usuário estiver com o jogo aberto em partida**, a menos que ele peça/autorize expressamente. Edições preparatórias devem ser feitas no código-fonte em `C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\AskaMultipleEye.cs`.
3. **BepInEx Config (`BepInEx\config\BepInEx.cfg`)**:
   - Manter sempre `PreloadIL2CPPInteropAssemblies = false` para evitar alertas do Controle Inteligente de Aplicativos do Windows em `UnityEngine.ShaderRuntimeModule.dll`.
4. **Segurança IL2CPP / Unity 6**:
   - **Nunca** ler `s.NetworkedCreatorUserId` enquanto `s._deserializing == true` ou `s.Object == null` ou `!s.Object.IsValid` (usar sempre `Plugin.IsNetworkObjectReady(s)`), pois acessar propriedades `[Networked]` do Photon Fusion durante a desserialização do save causa crash imediato ao carregar o mundo.
   - **Nunca** instanciar `new GUIStyle(...)` nem chamar `GUIStyle.set_fontStyle` (ICall removida/stripped no Unity 6 IL2CPP). Usar `GUI.Button(Rect, string)` de 2 argumentos.
   - **Nunca** aplicar Harmony Patch em métodos com colisão ICF (Identical Code Folding) no `GameAssembly.dll`. Sempre rodar `validate_stability.ps1` após compilar.

---

## 3. Arquitetura de Rede do ASKA e Soluções Definitivas

### 3.1. Vínculo Jogador ↔ Aldeia (`NetworkedCreatorUserId`)

- No ASKA original, existe apenas **1 único `Settlement` global** para todo o mapa.
- Para separar as aldeias estilo Age of Empires:
  - Toda `Structure` possui `NetworkedCreatorUserId` (gravado quando o jogador posiciona a planta no chão).
  - Em `IsStructureOwnedByLocalPlayer`, a comparação direta de `creatorKey == LocalUserKey` tem prioridade absoluta sobre flags locais (`LocallyPlacedStructure`), impedindo que o Host incorretamente "adote" minas ou cabanas construídas por clientes remotos.
  - Todo `VillagerOutlet` (Olho de Odin) possui `OwnerStructure` com o `NetworkedCreatorUserId` do jogador dono daquele Olho.
  - Todo `Villager` invocado em um Olho de Odin é vinculado permanentemente ao dono daquele Olho via `VillagerOwnerKeys[character]` (`VillagerOutlet.SpawnVillager`), persistindo mesmo se o Olho for desmontado ou destruído.
  - **Interação com Olho de Odin Estrangeiro**: `Patch_ActivationInteraction_CheckAgent` retorna `false` para impedir que um jogador abra o menu ou invoque aldeões no Olho de Odin de outro jogador.
  - **Regra de ouro dos Aldeões (`IsComponentInSameVillage` / `Buildstation` / `FindItemStorage`)**: Um aldeão pertencente ao jogador `A` **só pode interagir/construir/trabalhar/estocar** em estruturas cujo `CreatorUserId` seja do jogador `A` (sem limites artificiais de raio ou distância geométrica). Estruturas de outros jogadores são tratadas como neutras e ignoradas pela IA dos NPCs.

### 3.2. Desbloqueio Individual de Construções (Host vs. Cliente)

- **Problema descoberto**: O `NetworkBlueprintConditionsDatabase` do ASKA usa **1 único bit global por regra** na rede. Quando o Host constrói o Abrigo primeiro, o bit da regra do Olho de Odin vira `1` na rede. Se bloquearmos `FulfilNewBlueprintCondition` no Cliente, quando o Cliente constrói o Abrigo dele minutos depois, o bit da rede já é `1` e o evento de rede não dispara novamente — deixando o `conditionsData` do Cliente como `false` e travando o Olho de Odin do Cliente se os patches tiverem `if (!__result) return;`.
- **Solução definitiva**:
  - Não bloquear o armazenamento interno de `BlueprintConditionsDatabase`, mas fazer com que:
    - `NodeTemplate.IsUnlocked` (inclusive para o Olho de Odin, que exige o Abrigo!)
    - `BlueprintConditionsRule.CheckRequiredStructures`
    - `BlueprintConditionsRule.CheckRequiredPopulation`
    - `BlueprintConditionsRule.CheckAll`
    - `BlueprintConditionsRule.CheckWithData`
      definam **`__result = true` ou `false` diretamente** avaliando `AreRequirementsMetByLocalPlayer` (se o jogador local construiu os pré-requisitos dele e tem a população da vila dele), independentemente de o `__result` original do jogo base ser `true` ou `false`!

### 3.3. Validação no Servidor (`SettlementManager.Validate`)

- **Problema descoberto**: Quando o Jogador 2 (Cliente) posiciona uma construção, o **Host** executa `SettlementManager.Validate`. Se `Patch_SettlementManager_Validate` checar `IsTemplateUnlockedForLocalPlayer(template)`, o Host vai checar se o **Host** já desbloqueou aquela construção e vai destruir/apagar a construção do Jogador 2 caso o Jogador 2 tenha avançado antes do Host!
- **Solução definitiva**:
  - Em `Patch_SettlementManager_Validate`, **apenas relaxar a validação do Olho de Odin** (permitindo múltiplos Olhos de Odin no mundo). **NÃO** bloquear construções com `IsTemplateUnlockedForLocalPlayer` nem `LocalPlayerHasEye` dentro de `SettlementManager.Validate` (pois ele roda no Host para todos os clientes).
  - A trava de construção por jogador fica exclusivamente nas ferramentas locais de cada cliente: `BuildMenu`, `BuildItemsTabPage`, `StructureBuilder.Validate`, `StructureBuilder.ValidateCurrent`, `AnchorPointPlacementTool._ValidatePlacement` e `SkewedAnchorPointPlacementTool._ValidatePlacement`.

### 3.4. HUD, Painel do Aldeão, GPS/Mapa, Numeração e Idioma

1. **Painel de Invocação do Aldeão sem piscar (`WeatherWidget`)**:
   - No jogo base, tanto o evento de rede `WeatherWidget._OnUpcomingVillagerDataChanged` quanto `WeatherWidget.UpdateState` ativam `villagerEtaPanel.SetActive(true)` a cada frame/evento se `_upVillagerData.upcomingVillager == true`.
   - Se usarmos apenas `Postfix` para desligar, o jogo nativo liga e o mod desliga a 60 FPS, gerando o piscar (_flicker_).
   - **Solução Definitiva**:
     - `Patch_WeatherWidget_OnUpcomingVillagerDataChanged` com `Prefix` intercepta o evento, define `obj.upcomingVillager = false` e retorna `false` quando o jogador local não está invocando, impedindo o jogo nativo de ligar o painel.
     - `Patch_WeatherWidget_UpdateState` com `Prefix` define `__instance._upVillagerData.upcomingVillager = false` antes da execução original, garantindo que o método interno do jogo nunca decida ativar o painel na tela de quem não tem invocação no próprio Olho.
     - O `Patch_VillagerMenu_OnActivate` usa `Prefix` para desativar a notificação antes de renderizar se o aldeão não pertencer à vila do jogador local.
2. **GPS (Bússola), Minimapa e Mapa (`ObjectiveMarkerContainer`)**:
   - Filtrar `ObjectiveMarkerContainer.AddMarker(WorldObjectiveMarker m)` para **não adicionar** marcadores de estruturas (`m.structure`) que pertençam a outro jogador (`!IsStructureOwnedByLocalPlayer(m.structure)`) nem aldeões (`m.character`) de outra vila (`!IsVillagerInLocalPlayerVillage(v)`).
3. **Numeração Individual de Construções ("Abrigo 1")**:
   - Filtrar `Settlement.GetBuiltStructuresCount(StructureTemplate)` e `Settlement.IndexOfStructure(Structure)` para contar apenas as estruturas pertencentes ao mesmo `CreatorUserId`, garantindo que a primeira construção de cada jogador seja numerada como `1`.
4. **Nomes das Construções sempre em Português**:
   - Em `Patch_BlueprintConditionsRule_GetRemainingConditionsAsText`, usar `req.localizedName` e `req.Name` **antes** de `req.GetName()` (pois `GetName()` retorna o identificador interno em inglês).
5. **Comportamento Original do Olho de Odin no Menu de Construção após Construir (`BuildPreviewTabPage`)**:
   - Enquanto o jogador ainda não colocou o Olho de Odin dele (`!LocalPlayerHasEyeOrSite`), `template.uniqueBuilding = false` e `Settlement.CheckUniqueness = true` permitem posicionar o Olho de Odin no mapa.
   - Assim que o jogador posiciona o Olho de Odin dele (`LocalPlayerHasEyeOrSite = true` no `Postfix` de `PlayerBuilder.Build` / `StructureBuilder.Build`), `RelaxEyeTemplate` define `template.uniqueBuilding = true` (exceto durante `InProxySpawnValidation` no Host) e `Settlement.CheckUniqueness` retorna `false`.
   - Com isso, `BuildPreviewTabPage.RefreshConfirmButton` exibe exatamente a mensagem original do jogo informando que só é possível ter uma estrutura daquele tipo e desativa o botão de construir (`SelectForBuild` bloqueado), impedindo até de puxar o preview 3D no mapa — idêntico ao jogo sem mod.

### 3.5. Protocolo de Teste e Validação Limpa

1. **Recomendação de Teste em Novo Save (New Game)**:
   - Para validar todas as mecânicas sem interferência de saves antigos que registraram vilas pelo sistema geométrico obsoleto, iniciar sempre uma partida nova com o colega.
2. **Ciclo de Verificação em Gameplay**:
   - **Minuto 0**: Ambos entram sem estruturas liberadas.
   - **Desbloqueio**: O primeiro que construir Abrigo desbloqueia o Olho de Odin apenas para si; o outro continua com o Olho bloqueado até fazer seu próprio Abrigo.
   - **Construção do Olho de Odin**: Ao colocar o Olho no chão, o botão bloqueia na tela do dono (sem puxar armação 3D) e exibe o aviso nativo de unicidade, enquanto o outro jogador tem total liberdade para posicionar o seu próprio Olho.
   - **Minas Distantes e Postos Avançados**: A mina de um jogador pertence exclusivamente a ele; aldeões de outra vila ignoram completamente estruturas mineradoras ou postos remotos alheios.
   - **HUD**: O painel de contagem regressiva de aldeão chegando não pisca e só aparece na tela do jogador que estiver invocando no seu próprio Olho de Odin.

---

## 4. Caminhos Importantes do Projeto

- **Código-Fonte C#**: `C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\AskaMultipleEye.cs`
- **DLL Compilada do Mod**: `F:\Steam\steamapps\common\ASKA\BepInEx\plugins\AskaMultipleEye\AskaMultipleEye.dll`
- **Config do Mod**: `F:\Steam\steamapps\common\ASKA\BepInEx\config\AskaMultipleEye.cfg`
- **Config do BepInEx**: `F:\Steam\steamapps\common\ASKA\BepInEx\config\BepInEx.cfg`
- **Log em Tempo Real**: `F:\Steam\steamapps\common\ASKA\BepInEx\LogOutput.log`
- **Scripts de Build, Validação e GitHub**:
  - `C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\build_mod.ps1`
  - `C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\validate_stability.ps1`
  - `C:\Users\M41K\.gemini\antigravity\brain\821e8e65-fa14-4c39-b7a9-8ed48987209b\scratch\mirror_usb_to_github.ps1`
- **Repositório GitHub**: `https://github.com/MayconM41k/ModAskaMulti.git`
