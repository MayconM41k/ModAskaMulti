# Mod ASKA - Multi Aldeias (Olhos de Odin Independentes)

Este repositório contém **os mesmos arquivos prontos para jogar do Pen Drive**, atualizados para a versão **v1.0.1**.

---

### Novidades da Versão v1.0.1:
* **Separação Total desde o 1º Olho de Odin (Aldeões, HUD e Construções)**:
  - O isolamento de vilas agora ativa imediatamente a partir do **1º Olho de Odin** construído no mapa (ActiveEyePositions.Count >= 1).
  - **Olho de Odin Individual nos Pré-requisitos**: Construir o seu Olho de Odin **não** destrava mais as construções de Vila para os outros jogadores — cada jogador precisa levantar o seu próprio Olho de Odin para liberar a sua árvore de construções.
  - **Bloqueio de Replicação de Rede (FulfilNewBlueprintCondition)**: Impede que as construções feitas pelo Host destravem automaticamente estruturas no menu/diário dos convidados pela rede.
  - **Texto de Requisitos Completo ("Você deve construir: ...")**: Corrige o aviso vermelho vazio no menu de construção quando outro jogador já construiu o pré-requisito na vila dele, exibindo exatamente qual estrutura falta você construir.
  - **População e Notificação de Aldeão Exclusivas por Jogador**: Invocar um aldeão no seu Olho de Odin não soma mais na HUD do amigo (Aldeões: 0) e fecha automaticamente o pop-up com a foto do aldeão na tela de quem não é dono daquele Olho.
  - **Flag de Ativação Exclusiva no Menu Multiplayer**: Aparece no canto superior direito ao abrir as abas de Multiplayer (Criar, Hospedar ou Entrar) e desaparece ao iniciar a partida.

---

### Como Instalar (Para Amigos / Colegas):
1. Clique no botão verde **Code** no topo direito desta página e escolha **Download ZIP**.
2. Abra o arquivo ZIP baixado.
3. Extraia todas as pastas e arquivos diretamente dentro da pasta onde o seu jogo ASKA está instalado na Steam:
   * Exemplo padrão: C:\Program Files (x86)\Steam\steamapps\common\ASKA\
   *(Se o Windows perguntar, confirme para substituir os arquivos).*
4. Inicie o jogo normalmente pela Steam e clique na Flag no canto superior direito no Menu Multiplayer (ou aperte **F7**) para ativar o mod!

---

### O que está incluído no pacote:
- **winhttp.dll, doorstop_config.ini, .doorstop_version**: Inicializadores do BepInEx para Unity 6 / IL2CPP.
- **dotnet/**: Runtime do .NET necessário para o jogo carregar os mods.
- **BepInEx/**:
  - core/: Motor do BepInEx e Harmony.
  - interop/: Bibliotecas do jogo geradas para compatibilidade.
  - config/AskaMultipleEye.cfg: Configurações seguras do mod.
  - plugins/AskaMultipleEye/AskaMultipleEye.dll: Arquivo do mod (v1.0.1).