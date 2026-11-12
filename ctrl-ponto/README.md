# CTRL Ponto — código-fonte

Controle de jornada da gráfica. Python + pywebview (WebView2), interface em HTML/CSS/JS com o visual do ERP.

| Arquivo | O que faz |
|---|---|
| `core.py` | Dados (`%LOCALAPPDATA%\CtrlPonto\dados.json`, mesmo formato da versão anterior; campos desconhecidos são preservados), jornadas, saldos, feriados de SP, histórico, fechamento. |
| `app.py` | Janela, ponte com a interface, bandeja (pystray), instância única, início com o Windows, PDF (Chromium `Page.printToPDF`). |
| `ui/erp.css` | Visual compartilhado com o CTRL 3D (cópia de `CTRL 3D\Codigo-fonte\ui\erp.css`). Mude nos dois. |
| `ui/style.css`, `ui/app.js`, `ui/index.html` | Telas do CTRL Ponto e a folha A4. |
| `gerar_exe.bat` | Gera `CTRL Ponto.exe` na pasta principal. |

Rodar sem gerar o .exe: `python app.py`. Testar com outra pasta de dados: `set CTRLPONTO_DIR=C:\pasta\teste`.

Regras: saldo só em jornadas completas (4 horários, ou 2 em meia jornada até 6 h); a meta é guardada no dia ao registrar a entrada;
ajuste manual exige motivo e grava antes/depois no histórico; a entrada conta `AjusteEntradaMin` minutos antes do clique.
Conferido: outubro/2026 dá os mesmos totais do PDF da versão anterior (192:09, +04:09, 26 dias completos).
