# Mod ASKA - Multi Aldeias (Olhos de Odin Independentes)

Este repositório contém **os mesmos arquivos prontos para jogar do Pen Drive**, atualizados para a versão **v1.0.2** (Arquitetura de Aldeias por Jogador estilo *Age of Empires*).

---

### Novidades da Versão v1.0.2:
* **Aldeias Vinculadas ao Jogador (NetworkedCreatorUserId - Estilo Age of Empires)**:
  - Cada construção pertence individualmente ao jogador que a colocou (Structure.NetworkedCreatorUserId), enquanto qualquer jogador aliado pode ajudar a construir, depositar materiais ou usar as bancadas.
* **Desbloqueio Garantido do Olho de Odin e Construções para o Jogador 2 (Cliente)**:
  - Corrige o bug do Photon Fusion onde o bit de rede de 1-bit (NetworkBlueprintConditionsDatabase) impedia que o Jogador 2 desbloqueasse o **Olho de Odin** após construir o **Abrigo** se o Host já tivesse construído um Abrigo antes.
  - A árvore de construções (NodeTemplate.IsUnlocked) agora avalia diretamente as estruturas construídas pelo jogador local.
* **Fim das Construções Sumindo no Cliente (NetworkSpawner)**:
  - Corrige a validação no Host (_CanProxyStructureBeSafelySpawned, SettlementManager.Validate e Settlement.CheckUniqueness) para que o servidor nunca destrua a primeira construção colocada pelo Jogador 2.
* **Isolamento Completo de Aldeões (BuildAndSupplyQuest, LightkeepingQuest, RepairQuest, Workstation)**:
  - Aldeões da sua aldeia nunca mais atravessam o mapa para construir, abastecer, acender fogo ou reparar estruturas de outro jogador, mesmo quando só existe 1 Olho de Odin construído no mundo.
* **Bússola (GPS), Mapa (M) e Numeração ("Abrigo 1") Individuais**:
  - Oculta ícones de construções e aldeões de outros jogadores na Bússola e no Mapa (ObjectiveMarkerContainer).
  - A contagem e numeração das estruturas (Settlement.IndexOfStructure / GetBuiltStructuresCount) conta apenas as construções do próprio jogador (seu primeiro abrigo será sempre **Abrigo 1**).
* **HUD de Invocação sem Piscar & Textos 100% Traduzidos**:
  - Elimina o piscar (licker) do painel de invocação de aldeão (WeatherWidget.UpdateState) na tela de quem não está invocando e prioriza localizedName nos requisitos de construção.

---

### Como Instalar / Atualizar (Para Amigos / Colegas):
1. Clique no botão verde **Code** no topo direito desta página e escolha **Download ZIP** (ou baixe apenas BepInEx/plugins/AskaMultipleEye/AskaMultipleEye.dll se já tiver a v1.0.0/v1.0.1 instalada).
2. Extraia diretamente dentro da pasta onde o seu jogo ASKA está instalado na Steam:
   * Exemplo padrão: C:\Program Files (x86)\Steam\steamapps\common\ASKA\
   *(Confirme para substituir os arquivos).*
3. Inicie o jogo normalmente pela Steam e marque a Flag no canto superior direito no Menu Multiplayer (ou aperte **F7**) para ativar o mod!