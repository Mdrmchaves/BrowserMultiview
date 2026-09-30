# BrowserMultiview

App desktop para Windows que mostra vários painéis web lado a lado (ou empilhados) numa única janela. Cada painel tem sessão isolada (cookies, localStorage, IndexedDB), então dá para ter, por exemplo, duas contas diferentes do WhatsApp Web abertas ao mesmo tempo.

## Requisitos

- Windows 10/11 com o [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (já vem no Windows 11)
- .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`

## Como rodar

```
dotnet run --project src/BrowserMultiview
```

Ou abra `BrowserMultiview.sln` no Visual Studio 2026 (com a carga de trabalho "Desenvolvimento para desktop com .NET") e aperte F5. Abra pela solução, não pela pasta.

## Gerar o exe

```
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Run
```

Gera um único `BrowserMultiview.exe` (~1,4 MB) em `%LOCALAPPDATA%\Programs\BrowserMultiview` e abre o app (`-Run`). Aponte seu atalho para esse exe; rodar o script de novo atualiza o mesmo arquivo. O app precisa estar fechado (o script avisa). Requer o runtime do .NET 10 Desktop no PC; com `-SelfContained` o exe roda sem .NET instalado, mas fica bem maior.

O ícone fica em `src\BrowserMultiview\Assets\app.ico` e é gerado por `tools\make-icon.ps1`.

## Uso

- **Barras ocultas:** encoste o mouse na borda de cima da janela (ou no topo de um painel) e as barras aparecem por cima da página, sem empurrá-la. Com "Ocultar barras automaticamente" desligado, elas ficam fixas acima das páginas.
- **+ Painel / ✕:** adiciona ou remove um painel. Remover apaga também os dados do painel (login, cookies, cache). **Alternar layout:** lado a lado ↔ empilhado.
- **Divisor:** arraste para mudar a proporção entre os painéis.
- **Não lidas:** o total de conversas não lidas (somando os painéis) aparece numa bolinha vermelha no ícone da barra de tarefas e no título da janela. O número vem do título da página, ex.: "(3) WhatsApp"; conversas que o site não conta no título (como silenciadas no WhatsApp) não entram.
- **Tela cheia:** F11 (ou o botão "Tela cheia") esconde a barra de título e cobre a tela inteira, como no navegador. F11 de novo volta ao normal. Não fica salvo: o app abre sempre fora da tela cheia, do jeito (maximizado ou não) em que estava antes.
- **Zoom por painel:** botão com a porcentagem na barra do painel, ou Ctrl + / Ctrl - / Ctrl + roda do mouse.
- **Endereço:** só http/https; sem esquema, o app usa `https://`.
- Links que pedem nova janela abrem no navegador padrão do sistema. Popups com tamanho definido (o jeito comum de abrir login "Entrar com Google" etc.) abrem numa janela do app, no mesmo perfil do painel, mostrando o endereço no topo.

Layout, proporções, URLs, zoom e posição/tamanho da janela são salvos automaticamente (~1 s após cada mudança).

## Onde ficam os dados

Tudo em `%LOCALAPPDATA%\BrowserMultiview`:

| Caminho | Conteúdo |
|---|---|
| `workspace.json` | Painéis, layout, zoom e janela. Se estiver corrompido, o app volta ao padrão e guarda o arquivo como `workspace.json.corrupt-<data>`. |
| `webview\EBWebView\WV2Profile_pane-<id>\` | Perfil de cada painel (logins, cookies, cache). O `<id>` é o `Id` do painel no `workspace.json`. |

Para "deslogar" um painel do zero, remova-o e adicione outro. Na inicialização, pastas de perfil de painéis que não existem mais são apagadas (só quando o `workspace.json` foi lido com sucesso). Para voltar ao estado inicial, apague a pasta `BrowserMultiview` inteira.

## Limitações conhecidas

- Um site que abra o popup de login **sem** tamanho definido vai para o navegador do sistema e o login não volta para o app.
- As barras sobrepostas são janelas separadas: ao mover ou redimensionar a janela principal elas se fecham e reaparecem no próximo passar do mouse. Elas só aparecem com o app em primeiro plano.
