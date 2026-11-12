# CTRL 3D — código-fonte

Programa de custos, prazos e pedidos de impressão 3D (Anycubic Kobra X).
Janela própria com interface em HTML/CSS/JS (pywebview + WebView2); lógica e banco em Python.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `core.py` | Núcleo sem interface: banco SQLite, configurações, leitura dos arquivos do fatiador (`.gcode`, `.gcode.3mf`, `.bgcode`), cálculo de custo/prazo/mesa, Excel. |
| `app.py` | Janela do programa e a ponte (`Api`) que a interface chama. |
| `ui/index.html` | Estrutura das telas. |
| `ui/style.css` | Visual (cores da marca CTRL 3D). |
| `ui/app.js` | Comportamento das telas (etapas, tabelas, arrastar arquivo). |
| `assets/` | Ícone e logo. |
| `gerar_exe.bat` / `gerar_exe.ps1` | Gera o `CTRL 3D.exe` na pasta principal. |

## Rodar sem gerar o .exe

```
python app.py
```

Usa o banco em `..\Dados\ctrl3d_dados.db`. Para testar com outro banco: `set CTRL3D_DB=C:\caminho\teste.db`.

## Gerar o programa

Uma vez: `pip install pywebview openpyxl pillow pyinstaller`
Depois: dois cliques em `gerar_exe.bat`. O resultado (`CTRL 3D.exe` + pasta escondida `_internal`) vai para a pasta principal. Os dados não são tocados.

## Pontos importantes

- **Conta por peça:** o arquivo informa tempo e gramas de uma mesa; `calcular_pedido` divide pelas peças do arquivo e multiplica pela quantidade, somando aquecimento por impressão e troca de mesa.
- **Mesa:** `capacidade_mesa` usa o retângulo da peça, folga entre peças e margem na borda (Configurações).
- **Tamanho da peça:** contorno do fatiador (`objects_info`, `EXCLUDE_OBJECT_DEFINE`, `plate_1.json`); sem ele, mede pelo caminho do bico (`_caixa_por_movimentos`). O `.bgcode` é descompactado (Heatshrink + MeatPack).
- **Banco:** tabelas `config`, `filamentos`, `canais`, `pedidos`, `produtos`, `gastos`, `pessoal`. Colunas novas são criadas sozinhas ao abrir (ver `Banco.__init__`).
