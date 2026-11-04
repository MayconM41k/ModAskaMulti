# Mod ASKA - Multi Aldeias (Olhos de Odin Independentes)

Este repositÃ³rio contÃ©m **os mesmos arquivos prontos para jogar do Pen Drive**, atualizados para a versÃ£o **v0.9.9**.

---

### Novidades da VersÃ£o v0.9.9:
* **OtimizaÃ§Ã£o Extrema de Desempenho (FPS Restaurado)**:
  - Eliminadas todas as buscas ativas no grafo de cena que estavam rodando a cada quadro na interface (OnGUI) e na atualizaÃ§Ã£o (Update).
  - Cache inteligente de telas e instÃ¢ncias de controle: durante o jogo na partida, a interface de controle do mod permanece em espera com custo zero de processamento (0 ms).
  - Intervalos de sincronizaÃ§Ã£o de vilas rebalanceados para evitar engasgos em mundos grandes com muitas estruturas.
* **BotÃ£o/Flag de AtivaÃ§Ã£o no Canto Superior Direito**: BotÃ£o visual nas telas de menu/criaÃ§Ã£o multiplayer para ligar ou desligar o mod instantaneamente (atalho: tecla **F7**).
* **Progresso e DiÃ¡rio 100% Individuais por Jogador**: Desbloqueios e receitas estritamente vinculados ao que cada jogador construiu.

---

### Como Instalar (Para Amigos / Colegas):
1. Clique no botÃ£o verde **Code** no topo direito desta pÃ¡gina e escolha **Download ZIP**.
2. Abra o arquivo ZIP baixado.
3. Extraia todas as pastas e arquivos diretamente dentro da pasta onde o seu jogo ASKA estÃ¡ instalado na Steam:
   * Exemplo padrÃ£o: C:\Program Files (x86)\Steam\steamapps\common\ASKA\
   *(Se o Windows perguntar, confirme para substituir os arquivos).*
4. Inicie o jogo normalmente pela Steam!

---

### O que estÃ¡ incluÃ­do no pacote:
- **winhttp.dll, doorstop_config.ini, .doorstop_version**: Inicializadores do BepInEx para Unity 6 / IL2CPP.
- **dotnet/**: Runtime do .NET necessÃ¡rio para o jogo carregar os mods.
- **BepInEx/**:
  - core/: Motor do BepInEx e Harmony.
  - interop/: Bibliotecas do jogo geradas para compatibilidade.
  - config/AskaMultipleEye.cfg: ConfiguraÃ§Ãµes seguras do mod.
  - plugins/AskaMultipleEye/AskaMultipleEye.dll: Arquivo do mod (v0.9.9).
- **LEIA-ME_COMO_INSTALAR.txt**: InstruÃ§Ãµes rÃ¡pidas em texto.