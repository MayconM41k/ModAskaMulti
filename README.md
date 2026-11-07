# Mod ASKA - Multi Aldeias (Olhos de Odin Independentes)

Este repositÃ³rio contÃ©m **os mesmos arquivos prontos para jogar do Pen Drive**, atualizados para a versÃ£o **v1.0.0**.

---

### Novidades da VersÃ£o v1.0.0:
* **Carregamento de Mundo 100% Seguro e Nativo**:
  - O jogo sempre inicia com o mod **DESATIVADO** por padrÃ£o, garantindo que a abertura do jogo e o carregamento do mundo ocorram 100% de forma original (vanilla).
  - O mod passa a funcionar apenas quando vocÃª clicar no **BotÃ£o/Flag no Canto Superior Direito** (ou apertar **F7**).
  - ProteÃ§Ã£o completa contra leitura prematura de propriedades de rede (NetworkedCreatorUserId) durante a desserializaÃ§Ã£o do mundo (_deserializing), eliminando fechamentos durante o carregamento do mapa.
  - RemoÃ§Ã£o de chamadas GUIStyle nativas removidas na Unity 6 IL2CPP, utilizando exclusivamente chamadas de botÃ£o nativas suportadas.
* **Progresso e DiÃ¡rio 100% Individuais por Jogador (Estilo Age of Empires)**: Desbloqueios e receitas estritamente vinculados ao que cada jogador construiu, mantendo o compartilhamento fÃ­sico das bancadas.

---

### Como Instalar (Para Amigos / Colegas):
1. Clique no botÃ£o verde **Code** no topo direito desta pÃ¡gina e escolha **Download ZIP**.
2. Abra o arquivo ZIP baixado.
3. Extraia todas as pastas e arquivos diretamente dentro da pasta onde o seu jogo ASKA estÃ¡ instalado na Steam:
   * Exemplo padrÃ£o: C:\Program Files (x86)\Steam\steamapps\common\ASKA\
   *(Se o Windows perguntar, confirme para substituir os arquivos).*
4. Inicie o jogo normalmente pela Steam e clique na Flag no canto superior direito (ou aperte **F7**) para ativar o mod!

---

### O que estÃ¡ incluÃ­do no pacote:
- **winhttp.dll, doorstop_config.ini, .doorstop_version**: Inicializadores do BepInEx para Unity 6 / IL2CPP.
- **dotnet/**: Runtime do .NET necessÃ¡rio para o jogo carregar os mods.
- **BepInEx/**:
  - core/: Motor do BepInEx e Harmony.
  - interop/: Bibliotecas do jogo geradas para compatibilidade.
  - config/AskaMultipleEye.cfg: ConfiguraÃ§Ãµes seguras do mod.
  - plugins/AskaMultipleEye/AskaMultipleEye.dll: Arquivo do mod (v1.0.0).