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

## Uso

- **Barras ocultas:** encoste o mouse na borda de cima da janela (ou do topo de um painel) para mostrar a barra principal e as barras de endereço. Dá para desligar em "Ocultar barras automaticamente".
- **+ Painel / ✕:** adiciona ou remove um painel. **Alternar layout:** lado a lado ↔ empilhado.
- **Divisor:** arraste para mudar a proporção entre os painéis.
- **Zoom por painel:** botão com a porcentagem na barra do painel, ou Ctrl + / Ctrl - / Ctrl + roda do mouse.
- **Endereço:** só http/https; sem esquema, o app usa `https://`.
- Links que pedem nova janela abrem no navegador padrão do sistema.

Layout, proporções, URLs, zoom e posição/tamanho da janela são salvos automaticamente (~1 s após cada mudança).

## Onde ficam os dados

Tudo em `%LOCALAPPDATA%\BrowserMultiview`:

| Caminho | Conteúdo |
|---|---|
| `workspace.json` | Painéis, layout, zoom e janela. Se estiver corrompido, o app volta ao padrão e guarda o arquivo como `workspace.json.corrupt-<data>`. |
| `webview\EBWebView\WV2Profile_pane-<id>\` | Perfil de cada painel (logins, cookies, cache). O `<id>` é o `Id` do painel no `workspace.json`. |

Para "deslogar" um painel do zero, feche o app e apague a pasta `WV2Profile_pane-<id>` dele. Para voltar ao estado inicial, apague a pasta `BrowserMultiview` inteira.

## Limitações conhecidas

- Remover um painel **não** apaga a pasta de perfil dele (TODO).
- Logins que dependem de popup OAuth (`window.opener`) não funcionam, porque o popup abre no navegador do sistema.
- As barras, ao aparecer, empurram as páginas para baixo em vez de ficar por cima: o WPF não consegue desenhar sobre o WebView2.
