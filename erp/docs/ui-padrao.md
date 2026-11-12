# Padrão de UX/UI – ERP Gráfica CACP
Leia antes de criar ou alterar telas. Reaproveite o que existe; não crie estilos, cores ou componentes paralelos.

## Tokens
- **Cor:** só pincéis do tema (`App.xaml`, trocados pelo `ThemeManager` claro/escuro): `Paper` `Surface` `SurfaceAlt` `Ink` `Muted` `Line` `Accent`/`AccentSoft`/`AccentInk`/`OnAccent` `Danger(Soft)` `Warning(Soft)` `Info(Soft)` `Hover` `Selected`. Nunca cor fixa no XAML.
- **Status no código:** `ThemeManager.Get("COk"|"CWarn"|"CDanger"|"CInfo"|"CViolet")`. Pagamentos: PIX verde, Dinheiro âmbar, Débito azul, Crédito violeta.
- **Tipo de registro:** `Kind*` (+`Soft`): lançamento verde da marca, pedido azul-claro (`KPed*`); os dois iguais nos modos Viva e Sóbria, produto violeta, cliente azul-petróleo, compra laranja, lembrete dourado, recibo ardósia.
- **Tipografia:** estilos `Title`, `SectionTitle`, `Eyebrow`, `Muted`; tamanho escala por `ThemeManager.TextScale` (1–1,4). Dinheiro com números tabulares.
- **Espaço/forma:** sem tokens de espaçamento; siga os componentes (cartão `Padding 16,14`, cantos 8–11). Não reinvente margens por tela.

## Componentes (`Wpf/Interface/Componentes`, `Wpf/Interface`)
- `DialogFrame` + `DialogKit` (`LabeledField`, `ShowError`, `Footer`, `Summary`) para todo diálogo; `SectionCard`/`Card`; `KpiCard`/`KpiTile` (faixa de cor no topo: `KpiCap` + `KpiCapTile`, igual ao Início); `SearchShortcut.IsTarget` no campo de pesquisa da tela (Ctrl+F e etiqueta "Ctrl F"); `ViewToggle` (Cards ↔ Lista); `CommandPalette` (Ctrl+K); `KeyboardFlow` (Enter avança, Esc fecha); `RowActions`; `UiIcon` (Lucide).
- **Tabelas:** valores com `MoneyCell`/`MoneyHeader` à direita; texto longo `TrimCell` (reticências + tooltip); paginação/“carregar mais” pelo `ModuleLoader`; páginas embutidas não mostram “Atualizado”.
- **Badges:** `StatusTag` (`Tone`: ok, warn, danger, info, violet, neutral, late, ready, now) e `StatusTag.Column` nas grades; `Chip` para filtros.
- **Botões:** um `PrimaryButton` por área; `SoftButton`/`GhostButton`/`QuietButton` secundários; `DangerButton` sempre com confirmação; `IconButton` sempre com `ToolTip`.
- **Modais:** diálogo central (`DialogFrame`); avisos e confirmações só com `AlertWindow.Info/Confirm` (ação irreversível: foco em Cancelar, Enter não confirma). Gaveta lateral só para consulta. Esc fecha; Esc de novo volta ao Início.
- **Estados:** carregando = `Skeleton`; vazio = `EmptyState` com ação sugerida; erro de campo = `FieldError` junto do campo; erro geral = `AlertWindow` ou status do `ModuleLoader`. A 1ª carga da tela não espera debounce (só a digitação).
- **Toasts:** `Toast.Show(owner, msg, tone)` para confirmação rápida, sem janela.
- **Animação:** `Motion` (`Ui.cs`): 150–450 ms ease-out, só opacidade/posição (`Enter`, `Pop`, `Fade`, `CountTo`, `Stagger`, `Lift`); respeita `ThemeManager.MotionEnabled` (“Sem movimento”).

## PDFs (`Core/Relatorios/ReportWriter.cs`, `Wpf/Compartilhado/ReceiptWriter.cs`)
- Auxiliares próprios (`Tx`, `FillBox`, `StrokeBox`, `LineW`, `Money`, `TextWidth`); dados em `ReportWriter.Company`, logo `LogoPath`, carimbo `StampPath`.
- Ficha do pedido econômica em tinta (cabeçalho preto, só telefone/prazo em vermelho, quadro Sinal/Pago/Retirada para anotar à mão, totais coloridos). Recibos sóbrios (bons em preto e branco: caixas de valor com borda, só o saldo colorido) com carimbo sobre “Assinatura / Carimbo”. Fechamento com Confere/Falta/Sobra.

## Mobile (PWA, `Pwa/`, servida em `/app/`)
- Regras de celular só em `@media(max-width:1099px)`; acima disso nada muda. Toque ≥ 44 px, campos 16 px, sem rolagem horizontal.
- Barra inferior: Início, Pedidos, Produção, Calculadora, Mais. Helpers em `app.js`: `toast`, `sheet` (filtros/formulários), `state(...,'loading')`, `moreLoader`, `documentActions`/`share`, `countUp`.
- Estados iguais ao desktop (skeleton, vazio, erro com “Tentar novamente”); animações desligadas com `prefers-reduced-motion`.
- Ao mudar a PWA, suba a versão em `BrowserGateway.Version`, `app.js` e `sw.js`; arquivo novo entra em `AssetTypes` e no `SHELL`. O service worker nunca guarda dados da API.
