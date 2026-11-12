# Design system compartilhado

Os três sistemas usam o mesmo visual. A fonte da verdade é o ERP (WPF):
`erp/Wpf/Interface/ThemeManager.cs` (cores claro/escuro), `erp/Wpf/App.xaml` e `erp/Wpf/Interface/Componentes/`.
Os apps web (CTRL 3D e CTRL Ponto) reproduzem esse padrão em `ui/erp.css` — o mesmo arquivo nos dois.

## Cores

| Token | Claro | Escuro | Uso |
|---|---|---|---|
| `--paper` | `#F7F9F8` | `#121618` | fundo da janela |
| `--surface` | `#FFFFFF` | `#1B2124` | cartões, menu, barra superior |
| `--ink` | `#202A30` | `#E6ECEA` | texto |
| `--muted` | `#5B6970` | `#9AA8AE` | texto secundário |
| `--line` | `#DCE5E2` | `#313C40` | bordas |
| `--accent` | `#147A70` | `#2A9D90` | cor da marca, botão principal |
| `--danger` / `--warning` / `--info` | `#B54436` / `#8A5A12` / `#1C6480` | — | texto de alerta |
| `--c-ok` `--c-info` `--c-warn` `--c-violet` | vivas | — | faixas, ícones e barras |

Regra: cores escuras no **texto**; cores vivas só em **faixas, ícones e barras**.

## Componentes

- **Menu lateral** 232 px, grupos com rótulo, item atual com fundo suave e barra de 3 px.
- **Barra superior** com busca **Ctrl+K** (paleta de comandos), chip de status e um botão principal.
- **Cartão de indicador (KPI):** faixa de cor no topo, rótulo, valor grande e dica.
- **Seção numerada:** círculo de 26 px com o número na cor da marca.
- **Tabelas:** cabeçalho cinza, linhas de 40 px, valores à direita com números tabulares.
- **Etiquetas de status:** raio 6, 24 px (ok, aviso, perigo, info, violeta, neutro).
- **Diálogo central:** faixa de 5 px na cor do contexto, título 22, rodapé com Cancelar + ação. Ação irreversível começa com o foco em Cancelar.
- **Toast** escuro no rodapé para confirmações rápidas.
- Animações curtas (150–450 ms, só opacidade/posição) e respeito a "movimento reduzido".
